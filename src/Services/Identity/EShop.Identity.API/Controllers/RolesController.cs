using EShop.Identity.Application.Roles.Commands.AddUserToRole;
using EShop.Identity.Application.Roles.Commands.CreateRole;
using EShop.Identity.Application.Roles.Commands.DeleteRole;
using EShop.Identity.Application.Roles.Commands.RemoveUserFromRole;
using EShop.Identity.Application.Roles.Commands.UpdateRole;
using EShop.Identity.Application.Roles.Queries.GetRole;
using EShop.Identity.Application.Roles.Queries.GetRoles;
using EShop.Identity.Application.Roles.Queries.GetUsersInRole;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EShop.Identity.API.Controllers;

/// <summary>
/// Admin controller for managing roles.
///
/// Every action is a thin <c>_mediator.Send</c> plus a <c>ProblemForError</c> unwrap, matching
/// the other Identity controllers. It used to call <c>RoleManager</c>/<c>UserManager</c> inline
/// across all eight actions, which meant role mutations bypassed <c>ValidationBehavior</c>,
/// <c>LoggingBehavior</c>, <c>TransactionBehavior</c> **and** <c>CacheInvalidationBehavior</c> —
/// every other write in the service goes through all four. That was the structural reason SEC-02
/// (stale role cache) existed at all, and why its thirteen failure sites were hardcoded string
/// literals rather than <c>Result</c> unwraps.
///
/// The status code is still passed explicitly at every site rather than inferred from the error
/// code — see <see cref="ApiControllerBase"/> for why that contract matters here.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Authorize(Roles = "Admin")]
public class RolesController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public RolesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Get all roles
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<RoleResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RoleResponse>>> GetRoles(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetRolesQuery { Page = page, PageSize = pageSize }, cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : ProblemForError(result.Error!, StatusCodes.Status400BadRequest);
    }

    /// <summary>
    /// Get role by ID
    /// </summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(RoleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoleResponse>> GetRole(string id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetRoleQuery { RoleId = id }, cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : ProblemForError(result.Error!, StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// Create a new role
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(RoleResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RoleResponse>> CreateRole(
        [FromBody] CreateRoleCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return ProblemForError(result.Error!, StatusCodes.Status400BadRequest);
        }

        return CreatedAtAction(nameof(GetRole), new { id = result.Value!.Id }, result.Value);
    }

    /// <summary>
    /// Update a role
    /// </summary>
    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> UpdateRole(
        string id,
        [FromBody] UpdateRoleRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateRoleCommand { RoleId = id, Description = request.Description },
            cancellationToken);

        if (result.IsSuccess)
        {
            return NoContent();
        }

        // Discriminates on the error code because this action has two distinct failure statuses;
        // the status is still chosen here, not derived inside ProblemForError.
        var status = result.Error!.Code == "Role.NotFound"
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status400BadRequest;

        return ProblemForError(result.Error, status);
    }

    /// <summary>
    /// Delete a role
    /// </summary>
    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteRole(string id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new DeleteRoleCommand { RoleId = id }, cancellationToken);

        if (result.IsSuccess)
        {
            return NoContent();
        }

        var status = result.Error!.Code == "Role.NotFound"
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status400BadRequest;

        return ProblemForError(result.Error, status);
    }

    /// <summary>
    /// Get users in a role
    /// </summary>
    [HttpGet("{roleName}/users")]
    [ProducesResponseType(typeof(IReadOnlyList<UserInRoleResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<UserInRoleResponse>>> GetUsersInRole(
        string roleName,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetUsersInRoleQuery { RoleName = roleName, Page = page, PageSize = pageSize },
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : ProblemForError(result.Error!, StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// Add user to role
    /// </summary>
    [HttpPost("{roleName}/users/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> AddUserToRole(
        string roleName,
        string userId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new AddUserToRoleCommand { RoleName = roleName, UserId = userId },
            cancellationToken);

        if (result.IsSuccess)
        {
            return NoContent();
        }

        return ProblemForError(result.Error!, NotFoundOrBadRequest(result.Error!.Code));
    }

    /// <summary>
    /// Remove user from role
    /// </summary>
    [HttpDelete("{roleName}/users/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> RemoveUserFromRole(
        string roleName,
        string userId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new RemoveUserFromRoleCommand { RoleName = roleName, UserId = userId },
            cancellationToken);

        if (result.IsSuccess)
        {
            return NoContent();
        }

        return ProblemForError(result.Error!, NotFoundOrBadRequest(result.Error!.Code));
    }

    /// <summary>
    /// The membership endpoints have two "missing thing" failures (unknown user, unknown role)
    /// that are both 404, and everything else is a 400.
    /// </summary>
    private static int NotFoundOrBadRequest(string errorCode) =>
        errorCode is "Role.NotFound" or "User.NotFound"
            ? StatusCodes.Status404NotFound
            : StatusCodes.Status400BadRequest;
}

public record UpdateRoleRequest
{
    public string? Description { get; init; }
}
