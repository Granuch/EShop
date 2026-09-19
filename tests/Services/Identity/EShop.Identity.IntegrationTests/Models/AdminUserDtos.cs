namespace EShop.Identity.IntegrationTests.Admin;

/// <summary>
/// Response shapes for the admin user screens (Admin panel S6).
/// </summary>
/// <remarks>
/// <b><see cref="AdminUserSessionResponse"/> has no token field on purpose</b>, mirroring
/// <c>AdminUserSessionDto</c>. That makes it useless as a leak detector on its own — a DTO simply
/// ignores JSON members it does not declare — which is why
/// <c>SessionsResponse_ContainsNoTokenMaterial</c> asserts against the raw response string instead.
/// </remarks>
public sealed record AdminUserPage
{
    public List<AdminUserResponse> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
}

public sealed record AdminUserResponse
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
    public bool IsLockedOut { get; init; }
    public List<string> Roles { get; init; } = [];
}

public sealed record AdminUserDetailsResponse
{
    public string Id { get; init; } = string.Empty;
    public string? Email { get; init; }

    /// <summary>
    /// Added in S7 so <c>PUT /{id}/email</c>'s contract can be asserted: the user name has to move
    /// with the email, and a DTO that did not declare it would deserialize happily while the two
    /// silently diverged.
    /// </summary>
    public string? UserName { get; init; }

    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
    public string? ProfilePictureUrl { get; init; }
    public DateTimeOffset? LockoutEnd { get; init; }
    public bool EmailConfirmed { get; init; }
    public bool TwoFactorEnabled { get; init; }
    public bool IsActive { get; init; }
    public bool IsDeleted { get; init; }
    public DateTime? DeletedAt { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public string? LastLoginIp { get; init; }
    public bool IsLockedOut { get; init; }
    public int AccessFailedCount { get; init; }
    public bool HasGoogleLogin { get; init; }
    public bool HasGitHubLogin { get; init; }
    public List<string> Roles { get; init; } = [];
}

public sealed record AdminUserSessionResponse
{
    public Guid Id { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public string? CreatedByIp { get; init; }
    public DateTime? RevokedAt { get; init; }
    public string? RevokeReason { get; init; }
    public bool IsActive { get; init; }
}

public sealed record AdminUserStatsResponse
{
    public int Total { get; init; }
    public int NewInPeriod { get; init; }
    public int Active { get; init; }
    public int Locked { get; init; }
    public int Unconfirmed { get; init; }
    public int Deleted { get; init; }
}
