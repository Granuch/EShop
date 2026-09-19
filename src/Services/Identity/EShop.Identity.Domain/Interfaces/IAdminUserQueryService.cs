namespace EShop.Identity.Domain.Interfaces;

/// <summary>
/// Read-side projections for the admin user screens (Admin panel S6). Implemented in
/// Infrastructure, declared here so Application can depend on it without referencing
/// Infrastructure — the same rule <c>IRefreshTokenRepository</c>, <c>ITokenService</c> and
/// <c>ICachedUserRolesService</c> follow.
/// </summary>
/// <remarks>
/// Separate from <c>IUserRepository</c> on purpose: that is the write-side boundary and returns
/// <c>ApplicationUser</c> aggregates, while everything here is a projection shaped for one screen
/// and never materialises an entity. Keeping them apart is what stops a list endpoint from loading
/// a thousand tracked users to read four columns.
/// </remarks>
public interface IAdminUserQueryService
{
    /// <summary>
    /// One page of the admin user list, plus the total under the same filter.
    /// </summary>
    Task<(IReadOnlyList<AdminUserRow> Rows, int TotalCount)> GetUsersAsync(
        AdminUserFilter filter,
        AdminUserSortBy sortBy,
        bool isDescending,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One user's full row for the detail card, <b>including soft-deleted users</b>.
    /// </summary>
    /// <remarks>
    /// This is why the detail read does not go through <c>IUserRepository.GetByIdAsync</c>: that
    /// calls <c>UserManager.FindByIdAsync</c>, which runs under the <c>!u.IsDeleted</c> global
    /// query filter and so answers null for exactly the users an admin opens this card to inspect.
    /// Returns <see cref="AdminUserRow"/> extended with the detail-only columns.
    /// </remarks>
    Task<AdminUserDetailRow?> GetUserDetailAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The counts behind the dashboard tile, taken in one round trip.
    /// </summary>
    Task<AdminUserStats> GetUserStatsAsync(
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A user's refresh-token sessions, newest first.
    /// </summary>
    /// <remarks>
    /// <b>The projection must never carry <c>TokenHash</c> or <c>ReplacedByTokenHash</c>.</b> The
    /// raw token is not stored (SEC-04), but a hash is still a credential-equivalent for an offline
    /// attacker and has no business leaving the database. <see cref="AdminUserSession"/> has no
    /// field that could hold one; keep it that way.
    /// </remarks>
    Task<IReadOnlyList<AdminUserSession>> GetUserSessionsAsync(
        string userId,
        CancellationToken cancellationToken = default);
}

/// <param name="IsDeleted">
/// Tri-state, and the only way to see soft-deleted users: <c>null</c> and <c>false</c> both mean
/// live users only (the <c>!u.IsDeleted</c> global filter applies), <c>true</c> lifts that filter
/// <b>and</b> restricts the result to deleted users.
/// <para>
/// One flag rather than a pair, for the reason S4's <c>DeletedOnly</c> documents: "lift the filter"
/// and "show only deleted" are never wanted separately here, and two booleans would have a
/// meaningless fourth combination. There is deliberately no "all users, deleted included" option —
/// mixing them in one list gives every row's meaning a footnote, and the two audiences (an admin
/// managing users, an admin looking for something to restore) are different screens.
/// </para>
/// <para>
/// Unlike Catalog's equivalents this may be bound straight from the request, because the entire
/// controller is behind the Admin policy — there is no anonymous caller to protect against.
/// </para>
/// </param>
public sealed record AdminUserFilter(
    string? Search = null,
    string? Role = null,
    bool? IsActive = null,
    bool? IsDeleted = null,
    bool? EmailConfirmed = null,
    bool? TwoFactorEnabled = null,
    DateTime? CreatedFrom = null,
    DateTime? CreatedTo = null,
    DateTime? LastLoginFrom = null,
    DateTime? LastLoginTo = null);

public enum AdminUserSortBy
{
    CreatedAt,
    Email,
    LastLoginAt
}

/// <summary>One row of the admin user list. Roles are filled in separately, in one batch query.</summary>
public sealed record AdminUserRow
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
    public IReadOnlyList<string> Roles { get; init; } = [];
}

/// <summary>
/// The detail card's row. Carries no credential material: no password hash, no security stamp, no
/// 2FA secret, and external logins reduced to booleans — see <c>AdminUserDetailsDto</c> for why.
/// </summary>
public sealed record AdminUserDetailRow
{
    public string Id { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? UserName { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
    public string? ProfilePictureUrl { get; init; }
    public bool EmailConfirmed { get; init; }
    public bool PhoneNumberConfirmed { get; init; }
    public bool TwoFactorEnabled { get; init; }
    public bool IsActive { get; init; }
    public bool IsDeleted { get; init; }
    public DateTime? DeletedAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public string? LastLoginIp { get; init; }
    public bool LockoutEnabled { get; init; }
    public DateTimeOffset? LockoutEnd { get; init; }
    public int AccessFailedCount { get; init; }
    public bool HasGoogleLogin { get; init; }
    public bool HasGitHubLogin { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
}

public sealed record AdminUserStats(
    int Total,
    int NewInPeriod,
    int Active,
    int Locked,
    int Unconfirmed,
    int Deleted);

/// <summary>
/// One refresh-token session. Deliberately has no token, no hash, and no successor hash.
/// </summary>
public sealed record AdminUserSession(
    Guid Id,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    string? CreatedByIp,
    DateTime? RevokedAt,
    string? RevokedByIp,
    string? RevokeReason,
    bool IsActive);
