using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using EShop.Identity.Domain.Entities;

namespace EShop.Identity.API.Controllers;

/// <summary>
/// Admin controller for managing roles
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Roles = "Admin")]
public class RolesController : ApiControllerBase
{
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<RolesController> _logger;

    public RolesController(
        RoleManager<ApplicationRole> roleManager,
        UserManager<ApplicationUser> userManager,
        ILogger<RolesController> logger)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _logger = logger;
    }

    /// <summary>
    /// Get all roles
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<RoleResponse>), StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<RoleResponse>> GetRoles()
    {
        var roles = _roleManager.Roles.Select(r => new RoleResponse
        {
            Id = r.Id,
            Name = r.Name!,
            Description = r.Description
        });

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
        var users = await _userManager.GetUsersInRoleAsync(roleName);

        var response = users.Select(u => new UserInRoleResponse
        {
            Id = u.Id,
            Email = u.Email!,
            FirstName = u.FirstName,
            LastName = u.LastName
        });

        return Ok(response);
    }

    /// <summary>
    /// Add user to role
    /// </summary>
    [HttpPost("{roleName}/users/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> AddUserToRole(string roleName, string userId)
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

        _logger.LogInformation("User {UserId} added to role {RoleName}", userId, roleName);

        return NoContent();
    }

    /// <summary>
    /// Remove user from role
    /// </summary>
    [HttpDelete("{roleName}/users/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RemoveUserFromRole(string roleName, string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);

        if (user == null)
        {
            return ProblemForError("User.NotFound", "User not found", StatusCodes.Status404NotFound);
        }

        var result = await _userManager.RemoveFromRoleAsync(user, roleName);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return ProblemForError("Role.RemoveUserFailed", errors, StatusCodes.Status400BadRequest);
        }

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
