using MediatR;
using EShop.BuildingBlocks.Application;

namespace EShop.Identity.Application.Account.Queries.GetProfile;

/// <summary>
/// Query to get user profile
/// </summary>
public record GetProfileQuery : IRequest<Result<UserProfileResponse>>
{
    public string UserId { get; init; } = string.Empty;
}

public record UserProfileResponse
{
    public string Id { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? ProfilePictureUrl { get; init; }
    public bool EmailConfirmed { get; init; }
    public bool TwoFactorEnabled { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }
    public List<string> Roles { get; init; } = [];
}
