using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.Identity.Domain.Interfaces;
using MediatR;

namespace EShop.Identity.Application.Users.Queries.GetAdminUsers;

/// <summary>
/// The admin user list (Admin panel S6, endpoint #1). Identity had no way to enumerate users at
/// all before this — <c>UsersController</c> exposed one internal-service contact lookup by id.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every value type here is nullable, including the bools and ints.</b> This binds with
/// <c>[FromQuery]</c> on a controller rather than <c>[AsParameters]</c> on a minimal API, so the
/// hard failure the root guide documents does not apply — but the intent is the same and mixing
/// the two conventions across one service is how a required parameter appears by accident.
/// </para>
/// <para>
/// <b>Deliberately not cached.</b> Identity has no <c>CachingBehavior</c> in its pipeline for
/// queries of this shape, and an admin user list is exactly the read where staleness is worst: it
/// is opened to check the result of something the admin just did.
/// </para>
/// </remarks>
public record GetAdminUsersQuery : IRequest<Result<PagedResult<AdminUserDto>>>
{
    public string? Search { get; init; }
    public string? Role { get; init; }
    public bool? IsActive { get; init; }

    /// <summary>
    /// Tri-state. <c>null</c>/<c>false</c> list live users; <c>true</c> is the only value that
    /// reveals soft-deleted ones, and it restricts the list to them. See <c>AdminUserFilter</c>.
    /// </summary>
    public bool? IsDeleted { get; init; }

    public bool? EmailConfirmed { get; init; }
    public bool? TwoFactorEnabled { get; init; }
    public DateTime? CreatedFrom { get; init; }
    public DateTime? CreatedTo { get; init; }
    public DateTime? LastLoginFrom { get; init; }
    public DateTime? LastLoginTo { get; init; }
    public AdminUserSortBy? SortBy { get; init; }
    public bool? IsDescending { get; init; }
    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }

    public AdminUserSortBy EffectiveSortBy => SortBy ?? AdminUserSortBy.CreatedAt;

    /// <summary>Newest first by default: an admin list is read to find recent activity.</summary>
    public bool EffectiveIsDescending => IsDescending ?? true;

    public int EffectivePageNumber => PageNumber ?? 1;
    public int EffectivePageSize => PageSize ?? 20;
}

/// <summary>
/// One row of the admin user list. Carries no secret: no password hash, no security stamp, no
/// 2FA secret, no external-provider id — only what a list needs to render and filter on.
/// </summary>
public sealed record AdminUserDto
{
    public string Id { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? UserName { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public bool EmailConfirmed { get; init; }
    public bool TwoFactorEnabled { get; init; }
    public bool IsActive { get; init; }
    public bool IsDeleted { get; init; }
    public DateTime? DeletedAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public DateTimeOffset? LockoutEnd { get; init; }

    /// <summary>True while a lockout is in force, so a client need not compare clocks itself.</summary>
    public bool IsLockedOut { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = [];
}
