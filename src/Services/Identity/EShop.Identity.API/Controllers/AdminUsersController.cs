using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.Identity.Application.Users.Commands.ActivateUser;
using EShop.Identity.Application.Users.Commands.ChangeUserEmail;
using EShop.Identity.Application.Users.Commands.ConfirmUserEmail;
using EShop.Identity.Application.Users.Commands.CreateUser;
using EShop.Identity.Application.Users.Commands.DeactivateUser;
using EShop.Identity.Application.Users.Commands.DeleteUser;
using EShop.Identity.Application.Users.Commands.DisableUserTwoFactor;
using EShop.Identity.Application.Users.Commands.LockUser;
using EShop.Identity.Application.Users.Commands.ResetUserPassword;
using EShop.Identity.Application.Users.Commands.RestoreUser;
using EShop.Identity.Application.Users.Commands.RevokeUserTokens;
using EShop.Identity.Application.Users.Commands.SetUserRoles;
using EShop.Identity.Application.Users.Commands.UnlockUser;
using EShop.Identity.Application.Users.Commands.UpdateUserProfile;
using EShop.Identity.Application.Users.Queries.GetAdminUserDetails;
using EShop.Identity.Application.Users.Queries.GetAdminUserRoles;
using EShop.Identity.Application.Users.Queries.GetAdminUserSessions;
using EShop.Identity.Application.Users.Queries.GetAdminUsers;
using EShop.Identity.Application.Users.Queries.GetAdminUserStats;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EShop.Identity.API.Controllers;

/// <summary>
/// The admin panel's user screens: reads from S6 (endpoints #1, #2, #15, #18, #19) and the
/// writes from S7 (#3–#14, #16, #17).
/// </summary>
/// <remarks>
/// <para>
/// <b>Guarded by the <c>users.read</c> PERMISSION policy, not by a role — the first production use
/// of the S1 vocabulary.</b> Identity has no <c>"Admin"</c> policy at all (its only named policy is
/// <c>InternalService</c>, for service-to-service calls carrying an API key and no user), so
/// copying Catalog's <c>RequireAuthorization("Admin")</c> here would have thrown
/// <c>The AuthorizationPolicy named: 'Admin' was not found</c> at the first request. S1's
/// <c>AddEShopPermissions()</c> registers one policy per permission and the <c>Admin</c> role
/// bundles every one of them, so an existing admin token satisfies this unchanged while the
/// vocabulary stays available for finer grants later.
/// </para>
/// <para>
/// Note multiple <c>[Authorize]</c> attributes are **combined**, not overridden. S7 relies on that
/// rather than fighting it: every write additionally carries <c>users.manage</c> (and the role
/// editor <c>roles.manage</c>), so a write requires read <b>and</b> manage. That is the intended
/// reading — an operator who may change an account can obviously see it — and it keeps the
/// class-level attribute doing its real job, which is that an action added here is never anonymous
/// by omission.
/// </para>
/// <para>
/// <b>Nothing here returns credential material.</b> No password hash, no security stamp, no 2FA
/// secret, and — the one worth stating explicitly because the table has a column for it — no
/// refresh-token hash. See <c>AdminUserSessionDto</c>.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/admin/users")]
[Authorize(Policy = EShopPermissions.UsersRead)]
public class AdminUsersController : ApiControllerBase
{
    private readonly IMediator _mediator;

    public AdminUsersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Paged, filtered user list (#1).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AdminUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<AdminUserDto>>> GetUsers(
        [FromQuery] GetAdminUsersQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsFailure)
            return ProblemForError(result.Error!, StatusCodes.Status400BadRequest);

        return Ok(result.Value);
    }

    /// <summary>
    /// The dashboard tile (#19).
    /// </summary>
    /// <remarks>
    /// <b>Declared before <c>{id}</c> below, and the order is not what makes it work.</b> ASP.NET
    /// routing scores a literal segment above a parameter segment regardless of declaration order,
    /// so <c>stats</c> would win even if this action came last — and a test pins that
    /// (<c>StatsRoute_IsNotSwallowedByTheUserIdRoute</c>) rather than trusting the ordering.
    /// The reason to keep it first anyway is that the ordering rule is invisible in the source,
    /// and the next person adding a literal route should not have to rediscover it. Note the id is
    /// a string, not a Guid — ASP.NET Identity keys users by string — so there is no route
    /// constraint here to fall back on, which is exactly why this is worth a test.
    /// </remarks>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(AdminUserStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AdminUserStatsDto>> GetStats(
        [FromQuery] GetAdminUserStatsQuery query,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(query, cancellationToken);

        if (result.IsFailure)
            return ProblemForError(result.Error!, StatusCodes.Status400BadRequest);

        return Ok(result.Value);
    }

    /// <summary>One user's detail card (#2). Finds soft-deleted users too.</summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(AdminUserDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminUserDetailsDto>> GetUser(
        string id,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAdminUserDetailsQuery { UserId = id }, cancellationToken);

        if (result.IsFailure)
            return ProblemForError(result.Error!, StatusCodes.Status404NotFound);

        return Ok(result.Value);
    }

    /// <summary>A user's role names (#15).</summary>
    [HttpGet("{id}/roles")]
    [ProducesResponseType(typeof(IReadOnlyList<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<string>>> GetRoles(
        string id,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAdminUserRolesQuery { UserId = id }, cancellationToken);

        if (result.IsFailure)
            return ProblemForError(result.Error!, StatusCodes.Status404NotFound);

        return Ok(result.Value);
    }

    /// <summary>
    /// A user's refresh-token sessions (#18) — issued, expires, IP, revocation. <b>Never a token or
    /// its hash.</b>
    /// </summary>
    [HttpGet("{id}/sessions")]
    [ProducesResponseType(typeof(IReadOnlyList<AdminUserSessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AdminUserSessionDto>>> GetSessions(
        string id,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetAdminUserSessionsQuery { UserId = id }, cancellationToken);

        if (result.IsFailure)
            return ProblemForError(result.Error!, StatusCodes.Status404NotFound);

        return Ok(result.Value);
    }

    // ---------------------------------------------------------------------------------------
    // Writes (Admin panel S7). Every one of them adds users.manage on top of the class-level
    // users.read, which combine rather than override.
    // ---------------------------------------------------------------------------------------

    /// <summary>Creates an account on a user's behalf (#3).</summary>
    /// <remarks>
    /// Omit <c>password</c> to create the account without one and email a reset link instead; the
    /// response's <c>inviteSent</c> says which happened.
    /// </remarks>
    [HttpPost]
    [Authorize(Policy = EShopPermissions.UsersManage)]
    [ProducesResponseType(typeof(CreateUserResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateUserResponse>> CreateUser(
        [FromBody] CreateUserCommand command,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return ProblemForError(result.Error!, StatusFor(result.Error!.Code));

        return CreatedAtAction(nameof(GetUser), new { id = result.Value!.UserId }, result.Value);
    }

    /// <summary>Edits a user's profile (#4). Every field is optional — omitted leaves it alone.</summary>
    [HttpPut("{id}")]
    [Authorize(Policy = EShopPermissions.UsersManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult> UpdateUser(
        string id,
        [FromBody] UpdateUserProfileRequest request,
        CancellationToken cancellationToken)
        => SendAsync(new UpdateUserProfileCommand
        {
            UserId = id,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber,
            ProfilePictureUrl = request.ProfilePictureUrl
        }, cancellationToken);

    /// <summary>Changes a user's email, and their user name with it (#5).</summary>
    [HttpPut("{id}/email")]
    [Authorize(Policy = EShopPermissions.UsersManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult> ChangeEmail(
        string id,
        [FromBody] ChangeUserEmailRequest request,
        CancellationToken cancellationToken)
        => SendAsync(new ChangeUserEmailCommand
        {
            UserId = id,
            Email = request.Email,
            MarkConfirmed = request.MarkConfirmed
        }, cancellationToken);

    /// <summary>Re-enables a deactivated account (#6).</summary>
    [HttpPost("{id}/activate")]
    [Authorize(Policy = EShopPermissions.UsersManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult> Activate(string id, CancellationToken cancellationToken)
        => SendAsync(new ActivateUserCommand { UserId = id }, cancellationToken);

    /// <summary>Suspends an account and ends its sessions (#7).</summary>
    [HttpPost("{id}/deactivate")]
    [Authorize(Policy = EShopPermissions.UsersManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult> Deactivate(string id, CancellationToken cancellationToken)
        => SendAsync(new DeactivateUserCommand { UserId = id }, cancellationToken);

    /// <summary>Soft-deletes an account (#8).</summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = EShopPermissions.UsersManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult> Delete(string id, CancellationToken cancellationToken)
        => SendAsync(new DeleteUserCommand { UserId = id }, cancellationToken);

    /// <summary>Un-deletes an account (#9). It comes back deactivated — see decision Q1a.</summary>
    [HttpPost("{id}/restore")]
    [Authorize(Policy = EShopPermissions.UsersManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult> Restore(string id, CancellationToken cancellationToken)
        => SendAsync(new RestoreUserCommand { UserId = id }, cancellationToken);

    /// <summary>Locks an account out until a given moment (#10).</summary>
    [HttpPost("{id}/lock")]
    [Authorize(Policy = EShopPermissions.UsersManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult> Lock(
        string id,
        [FromBody] LockUserRequest request,
        CancellationToken cancellationToken)
        => SendAsync(new LockUserCommand
        {
            UserId = id,
            Until = request.Until,
            Reason = request.Reason
        }, cancellationToken);

    /// <summary>Lifts a lockout, including the brute-force counter (#11).</summary>
    [HttpPost("{id}/unlock")]
    [Authorize(Policy = EShopPermissions.UsersManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult> Unlock(string id, CancellationToken cancellationToken)
        => SendAsync(new UnlockUserCommand { UserId = id }, cancellationToken);

    /// <summary>Emails the user a password-reset link (#12). Takes no password — decision Q2a.</summary>
    [HttpPost("{id}/reset-password")]
    [Authorize(Policy = EShopPermissions.UsersManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult> ResetPassword(string id, CancellationToken cancellationToken)
        => SendAsync(new ResetUserPasswordCommand { UserId = id }, cancellationToken);

    /// <summary>Marks the address confirmed on an administrator's word (#13).</summary>
    [HttpPost("{id}/confirm-email")]
    [Authorize(Policy = EShopPermissions.UsersManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult> ConfirmEmail(string id, CancellationToken cancellationToken)
        => SendAsync(new ConfirmUserEmailCommand { UserId = id }, cancellationToken);

    /// <summary>Turns off a user's second factor without their code (#14).</summary>
    [HttpPost("{id}/disable-2fa")]
    [Authorize(Policy = EShopPermissions.UsersManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult> DisableTwoFactor(string id, CancellationToken cancellationToken)
        => SendAsync(new DisableUserTwoFactorCommand { UserId = id }, cancellationToken);

    /// <summary>
    /// Replaces a user's whole role set (#16).
    /// </summary>
    /// <remarks>
    /// The one action guarded by <c>roles.manage</c> rather than <c>users.manage</c>, because the
    /// vocabulary defines that permission as "create and delete roles, and change who is in them".
    /// Both still combine with the class-level <c>users.read</c>.
    /// </remarks>
    [HttpPut("{id}/roles")]
    [Authorize(Policy = EShopPermissions.RolesManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult> SetRoles(
        string id,
        [FromBody] SetUserRolesRequest request,
        CancellationToken cancellationToken)
        => SendAsync(new SetUserRolesCommand { UserId = id, Roles = request.Roles ?? [] }, cancellationToken);

    /// <summary>Revokes every refresh token the user holds (#17).</summary>
    [HttpPost("{id}/revoke-tokens")]
    [Authorize(Policy = EShopPermissions.UsersManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult> RevokeTokens(string id, CancellationToken cancellationToken)
        => SendAsync(new RevokeUserTokensCommand { UserId = id }, cancellationToken);

    /// <summary>
    /// The shared tail of every write: dispatch, and either 204 or a problem+json with the status
    /// the error code implies.
    /// </summary>
    private async Task<ActionResult> SendAsync(IRequest<Result<Unit>> command, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(command, cancellationToken);

        if (result.IsFailure)
            return ProblemForError(result.Error!, StatusFor(result.Error!.Code));

        return NoContent();
    }

    /// <summary>
    /// Error code to status code, in one place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written out rather than inferred from a <c>.NotFound</c> suffix the way Catalog's
    /// <c>ProblemForError</c> does it — Identity deliberately never derives status from a code,
    /// because <c>Auth.InvalidCredentials</c> has to stay 401 and a suffix rule cannot know that.
    /// </para>
    /// <para>
    /// <c>Role.NotFound</c> answers 404 even though the route names a user rather than a role,
    /// matching <c>RolesController.NotFoundOrBadRequest</c>, which already treats both "missing
    /// thing" codes that way on its membership endpoints. The alternative reading — the addressed
    /// resource exists, so it is a 400 about the body — is the one Catalog's category move took,
    /// and picking Identity's own precedent keeps the two role surfaces answering alike.
    /// </para>
    /// <para>
    /// <c>Validation.Failed</c> lands in the default 400 arm. It reaches here at all because these
    /// commands return the <b>generic</b> <c>Result&lt;Unit&gt;</c>: <c>ValidationBehavior</c>
    /// converts a failure into a <c>Result</c> for <c>Result&lt;T&gt;</c> and throws for the
    /// non-generic <c>Result</c>, so the same validator produces a different error code depending
    /// on the response type.
    /// </para>
    /// </remarks>
    private static int StatusFor(string errorCode) => errorCode switch
    {
        "User.NotFound" or "Role.NotFound" => StatusCodes.Status404NotFound,
        "User.EmailConflict" or "User.NotDeleted" => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest
    };
}

/// <summary>
/// #4. Every member is nullable and omitting one leaves the stored value alone (BUG-09's rule).
/// </summary>
public record UpdateUserProfileRequest
{
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? PhoneNumber { get; init; }
    public string? ProfilePictureUrl { get; init; }
}

/// <summary>#5.</summary>
public record ChangeUserEmailRequest
{
    public string Email { get; init; } = string.Empty;
    public bool MarkConfirmed { get; init; }
}

/// <summary>#10. <c>Until</c> is required; indefinite suspension is <c>deactivate</c>.</summary>
public record LockUserRequest
{
    public DateTimeOffset Until { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// #16. Nullable so an omitted member binds as "no roles" rather than tripping the non-nullable
/// initializer, and an explicit <c>[]</c> means the same thing — this is a replacement.
/// </summary>
public record SetUserRolesRequest
{
    public IReadOnlyList<string>? Roles { get; init; }
}
