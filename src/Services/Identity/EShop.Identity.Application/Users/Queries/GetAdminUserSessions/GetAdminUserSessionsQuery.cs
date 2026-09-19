using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Interfaces;
using MediatR;

namespace EShop.Identity.Application.Users.Queries.GetAdminUserSessions;

/// <summary>
/// A user's live and historical refresh-token sessions (Admin panel S6, endpoint #18), so an admin
/// can see where an account is signed in before deciding to revoke.
/// </summary>
public record GetAdminUserSessionsQuery : IRequest<Result<IReadOnlyList<AdminUserSessionDto>>>
{
    public string UserId { get; init; } = string.Empty;
}

/// <summary>
/// One session. <b>It has no token and no hash, and must never gain one.</b>
/// </summary>
/// <remarks>
/// The raw refresh token has not been stored since SEC-04 — only a SHA-256 of it, plus a hash of
/// whatever superseded it on rotation. A hash is still credential-equivalent to an offline
/// attacker, and this endpoint would put one on the wire for every live session of every user. The
/// projection in <c>AdminUserQueryService.GetUserSessionsAsync</c> names each field it returns for
/// exactly that reason; a field added here would need a matching one there, which is the moment to
/// stop. <c>SessionsResponse_ContainsNoTokenMaterial</c> asserts it against the raw JSON.
/// </remarks>
public sealed record AdminUserSessionDto(
    Guid Id,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    string? CreatedByIp,
    DateTime? RevokedAt,
    string? RevokedByIp,
    string? RevokeReason,
    bool IsActive);

public class GetAdminUserSessionsQueryHandler
    : IRequestHandler<GetAdminUserSessionsQuery, Result<IReadOnlyList<AdminUserSessionDto>>>
{
    private readonly IAdminUserQueryService _queryService;

    public GetAdminUserSessionsQueryHandler(IAdminUserQueryService queryService)
    {
        _queryService = queryService;
    }

    public async Task<Result<IReadOnlyList<AdminUserSessionDto>>> Handle(
        GetAdminUserSessionsQuery request,
        CancellationToken cancellationToken)
    {
        // The user is checked first so a wrong id answers 404 rather than an empty list — an empty
        // list is a meaningful answer ("this user has never signed in") and must not double as
        // "no such user".
        var user = await _queryService.GetUserDetailAsync(request.UserId, cancellationToken);
        if (user is null)
            return Result<IReadOnlyList<AdminUserSessionDto>>.Failure(
                new Error("User.NotFound", $"User with ID '{request.UserId}' was not found."));

        var sessions = await _queryService.GetUserSessionsAsync(request.UserId, cancellationToken);

        IReadOnlyList<AdminUserSessionDto> dtos = sessions
            .Select(s => new AdminUserSessionDto(
                s.Id, s.CreatedAt, s.ExpiresAt, s.CreatedByIp,
                s.RevokedAt, s.RevokedByIp, s.RevokeReason, s.IsActive))
            .ToList();

        return Result<IReadOnlyList<AdminUserSessionDto>>.Success(dtos);
    }
}
