using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace EShop.Identity.API.Controllers;

/// <summary>
/// Admin controller for managing roles
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Roles = "Admin")]
public class RolesController : ApiControllerBase
{
    /// <summary>
    /// Upper bound on rows returned by the two list endpoints. Neither had one, so both grew
    /// without limit with the data. This is a cap, not paging — Stage 7's CQRS rewrite is where
    /// real paging belongs; until then a bounded response beats an unbounded one.
    /// </summary>
    private const int MaxPageSize = 200;

    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICachedUserRolesService _cachedUserRoles;
    private readonly ILogger<RolesController> _logger;

    public RolesController(
        RoleManager<ApplicationRole> roleManager,
        UserManager<ApplicationUser> userManager,
        ICachedUserRolesService cachedUserRoles,
        ILogger<RolesController> logger)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _cachedUserRoles = cachedUserRoles;
        _logger = logger;
    }

    /// <summary>
    /// Get all roles
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<RoleResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<RoleResponse>>> GetRoles(CancellationToken cancellationToken)
    {
        // This used to return the IQueryable itself. Nothing had executed by the time the action
        // returned, so the query ran inside the serializer while the response was being written:
        // outside the action's exception handling, holding the DbContext open for the duration of
        // the write, and blocking synchronously on each enumeration step. Materialise here.
        var roles = await _roleManager.Roles
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Take(MaxPageSize)
            .Select(r => new RoleResponse
            {
                Id = r.Id,
                Name = r.Name!,
                Description = r.Description
            })
            .ToListAsync(cancellationToken);

        return Ok(roles);
    }

    /// <summary>
    /// Get role by ID
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(RoleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoleResponse>> GetRole(string id)
    {
        var role = await _roleManager.FindByIdAsync(id);

        if (role == null)
        {
            return ProblemForError("Role.NotFound", "Role not found", StatusCodes.Status404NotFound);
        }

        return Ok(new RoleResponse
        {
            Id = role.Id,
            Name = role.Name!,
            Description = role.Description
        });
    }

    /// <summary>
    /// Create a new role
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(RoleResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RoleResponse>> CreateRole([FromBody] CreateRoleRequest request)
    {
        if (await _roleManager.RoleExistsAsync(request.Name))
        {
            return ProblemForError("Role.Exists", "Role already exists", StatusCodes.Status400BadRequest);
        }

        var role = new ApplicationRole
        {
            Name = request.Name,
            Description = request.Description
        };

        var result = await _roleManager.CreateAsync(role);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return ProblemForError("Role.CreateFailed", errors, StatusCodes.Status400BadRequest);
        }

        _logger.LogInformation("Role created: {RoleName}", role.Name);

        return CreatedAtAction(nameof(GetRole), new { id = role.Id }, new RoleResponse
        {
            Id = role.Id,
            Name = role.Name!,
            Description = role.Description
        });
    }

    /// <summary>
    /// Update a role
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> UpdateRole(string id, [FromBody] UpdateRoleRequest request)
    {
        var role = await _roleManager.FindByIdAsync(id);

        if (role == null)
        {
            return ProblemForError("Role.NotFound", "Role not found", StatusCodes.Status404NotFound);
        }

        role.Description = request.Description;

        var result = await _roleManager.UpdateAsync(role);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return ProblemForError("Role.UpdateFailed", errors, StatusCodes.Status400BadRequest);
        }

        return NoContent();
    }

    /// <summary>
    /// Delete a role
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteRole(string id)
    {
        var role = await _roleManager.FindByIdAsync(id);

        if (role == null)
        {
            return ProblemForError("Role.NotFound", "Role not found", StatusCodes.Status404NotFound);
        }

        // Prevent deletion of system roles
        if (role.Name == "Admin" || role.Name == "User")
        {
            return ProblemForError("Role.CannotDelete", "Cannot delete system roles", StatusCodes.Status400BadRequest);
        }

        var result = await _roleManager.DeleteAsync(role);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return ProblemForError("Role.DeleteFailed", errors, StatusCodes.Status400BadRequest);
        }

        _logger.LogInformation("Role deleted: {RoleName}", role.Name);

        return NoContent();
    }

    /// <summary>
    /// Get users in a role
    /// </summary>
    [HttpGet("{roleName}/users")]
    [ProducesResponseType(typeof(IEnumerable<UserInRoleResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<UserInRoleResponse>>> GetUsersInRole(string roleName)
    {
        // GetUsersInRoleAsync already materialises, but it is unbounded — every member of a role
        // in one response. Capped for the same reason GetRoles is.
        var users = await _userManager.GetUsersInRoleAsync(roleName);

        var response = users
            .OrderBy(u => u.Email)
            .Take(MaxPageSize)
            .Select(u => new UserInRoleResponse
            {
                Id = u.Id,
                Email = u.Email!,
                FirstName = u.FirstName,
                LastName = u.LastName
            })
            .ToList();

        return Ok(response);
    }

    /// <summary>
    /// Add user to role
    /// </summary>
    [HttpPost("{roleName}/users/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> AddUserToRole(string roleName, string userId, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(userId);

        if (user == null)
        {
            return ProblemForError("User.NotFound", "User not found", StatusCodes.Status404NotFound);
        }

        if (!await _roleManager.RoleExistsAsync(roleName))
        {
            return ProblemForError("Role.NotFound", "Role not found", StatusCodes.Status404NotFound);
        }

        var result = await _userManager.AddToRoleAsync(user, roleName);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return ProblemForError("Role.AddUserFailed", errors, StatusCodes.Status400BadRequest);
        }

        // The roles cache has a 5-minute TTL and is read when minting a token, so without this
        // the new role is invisible for up to 5 minutes after the grant.
        // INTERIM: this controller has no CQRS layer, so it cannot use ICacheInvalidatingCommand
        // like every other write in the service. Stage 7 (DEBT-01) moves these actions behind
        // MediatR commands marked ICacheInvalidatingCommand — delete this call then.
        await _cachedUserRoles.InvalidateRolesCacheAsync(userId, cancellationToken);

        _logger.LogInformation("User {UserId} added to role {RoleName}", userId, roleName);

        return NoContent();
    }

    /// <summary>
    /// Remove user from role
    /// </summary>
    [HttpDelete("{roleName}/users/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RemoveUserFromRole(string roleName, string userId, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(userId);

        if (user == null)
        {
            return ProblemForError("User.NotFound", "User not found", StatusCodes.Status404NotFound);
        }

        // Same existence check AddUserToRole makes; without it a misspelled role name reports
        // 400 "role does not exist" from RemoveFromRoleAsync rather than 404.
        if (!await _roleManager.RoleExistsAsync(roleName))
        {
            return ProblemForError("Role.NotFound", "Role not found", StatusCodes.Status404NotFound);
        }

        var result = await _userManager.RemoveFromRoleAsync(user, roleName);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return ProblemForError("Role.RemoveUserFailed", errors, StatusCodes.Status400BadRequest);
        }

        // Revocation is the security-critical direction: without this the removed role stays
        // in the cache for up to 5 minutes and a token minted in that window still carries it.
        // INTERIM — see AddUserToRole; Stage 7 replaces this with ICacheInvalidatingCommand.
        await _cachedUserRoles.InvalidateRolesCacheAsync(userId, cancellationToken);

        _logger.LogInformation("User {UserId} removed from role {RoleName}", userId, roleName);

        return NoContent();
    }
}

public record RoleResponse
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}

public record CreateRoleRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}

public record UpdateRoleRequest
{
    public string? Description { get; init; }
}

public record UserInRoleResponse
{
    public string Id { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
}
