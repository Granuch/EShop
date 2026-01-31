using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace EShop.Identity.Application.Account.Queries.GetProfile;

/// <summary>
/// Handler for getting user profile
/// </summary>
public class GetProfileQueryHandler : IRequestHandler<GetProfileQuery, Result<UserProfileResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public GetProfileQueryHandler(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<Result<UserProfileResponse>> Handle(GetProfileQuery request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);

        if (user == null || user.IsDeleted)
        {
            return Result<UserProfileResponse>.Failure(new Error("Account.NotFound", "User not found"));
        }

        if (!user.IsActive)
        {
            return Result<UserProfileResponse>.Failure(new Error("Account.Disabled", "Account is disabled"));
        }

        var roles = await _userManager.GetRolesAsync(user);

        return Result<UserProfileResponse>.Success(new UserProfileResponse
        {
            Id = user.Id,
            Email = user.Email!,
            FirstName = user.FirstName,
            LastName = user.LastName,
            ProfilePictureUrl = user.ProfilePictureUrl,
            EmailConfirmed = user.EmailConfirmed,
            TwoFactorEnabled = user.TwoFactorEnabled,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            Roles = roles.ToList()
        });
    }
}
