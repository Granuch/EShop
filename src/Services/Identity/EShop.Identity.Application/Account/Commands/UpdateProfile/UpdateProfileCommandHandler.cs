using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Account.Commands.UpdateProfile;

/// <summary>
/// Handler for updating user profile
/// </summary>
public class UpdateProfileCommandHandler : IRequestHandler<UpdateProfileCommand, Result<UpdateProfileResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<UpdateProfileCommandHandler> _logger;

    public UpdateProfileCommandHandler(
        UserManager<ApplicationUser> userManager,
        ILogger<UpdateProfileCommandHandler> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<Result<UpdateProfileResponse>> Handle(UpdateProfileCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);
        if (user == null)
        {
            _logger.LogWarning("Profile update attempt for non-existent user. UserId={UserId}", request.UserId);
            return Result<UpdateProfileResponse>.Failure(new Error("Account.NotFound", "User not found"));
        }

        var oldFirstName = user.FirstName;
        var oldLastName = user.LastName;

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.ProfilePictureUrl = request.ProfilePictureUrl;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            _logger.LogError("Failed to update profile. UserId={UserId}, Errors={Errors}", request.UserId, errors);
            return Result<UpdateProfileResponse>.Failure(new Error("Account.UpdateFailed", errors));
        }

        _logger.LogInformation("Profile updated successfully. UserId={UserId}, Email={Email}, OldName={OldFirstName} {OldLastName}, NewName={NewFirstName} {NewLastName}",
            request.UserId, user.Email, oldFirstName, oldLastName, request.FirstName, request.LastName);

        return Result<UpdateProfileResponse>.Success(new UpdateProfileResponse
        {
            Message = "Profile updated successfully"
        });
    }
}
