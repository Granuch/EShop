using EShop.BuildingBlocks.Application;
using MediatR;

namespace EShop.Identity.Application.Users.Queries.GetAdminUserDetails;

/// <summary>
/// The admin user detail card (Admin panel S6, endpoint #2).
/// </summary>
public record GetAdminUserDetailsQuery : IRequest<Result<AdminUserDetailsDto>>
{
    public string UserId { get; init; } = string.Empty;
}

/// <summary>
/// Everything the detail card shows — and nothing that is a credential.
/// </summary>
/// <remarks>
/// <b>Deliberately absent, and each for its own reason:</b> <c>PasswordHash</c> and
/// <c>SecurityStamp</c> (credential material), <c>TwoFactorSecret</c> (a live second factor —
/// reading it would let an admin mint the user's codes), and <c>ConcurrencyStamp</c> (an EF
/// implementation detail that would invite a client to send it back). <c>GoogleId</c>/<c>GitHubId</c>
/// are reported only as booleans: whether an external login is linked is what an admin needs, while
/// the provider's subject identifier is a correlatable identity in someone else's system.
/// </remarks>
public sealed record AdminUserDetailsDto
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
    public bool IsLockedOut { get; init; }
    public int AccessFailedCount { get; init; }

    public bool HasGoogleLogin { get; init; }
    public bool HasGitHubLogin { get; init; }

    public IReadOnlyList<string> Roles { get; init; } = [];
}
