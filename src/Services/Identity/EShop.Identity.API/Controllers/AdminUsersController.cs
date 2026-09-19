using EShop.BuildingBlocks.Application.Pagination;
using EShop.BuildingBlocks.Infrastructure.Authorization;
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
/// The admin panel's user screens (Admin panel S6, endpoints #1, #2, #15, #18, #19). Read-only —
/// every write lands in S7.
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
/// Note multiple <c>[Authorize]</c> attributes are **combined**, not overridden, so an action here
/// must not add a second policy unless it genuinely requires both.
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
}
