using MediatR;
using EShop.BuildingBlocks.Application;

namespace EShop.Identity.Application.Account.Commands.UpdateProfile;

/// <summary>
/// Command to update user profile
/// </summary>
public record UpdateProfileCommand : IRequest<Result<UpdateProfileResponse>>
{
    public string UserId { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string? ProfilePictureUrl { get; init; }
}

public record UpdateProfileResponse
{
    public string Message { get; init; } = string.Empty;
}
