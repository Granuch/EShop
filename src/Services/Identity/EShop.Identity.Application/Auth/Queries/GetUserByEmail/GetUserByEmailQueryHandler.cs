using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace EShop.Identity.Application.Auth.Queries.GetUserByEmail;

/// <summary>
/// Handler for GetUserByEmailQuery
/// </summary>
public class GetUserByEmailQueryHandler : IRequestHandler<GetUserByEmailQuery, Result<UserByEmailResponse>>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public GetUserByEmailQueryHandler(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<Result<UserByEmailResponse>> Handle(GetUserByEmailQuery request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);

        if (user == null || user.IsDeleted)
        {
            return Result<UserByEmailResponse>.Failure(new Error("Auth.UserNotFound", "User not found"));
        }

        return Result<UserByEmailResponse>.Success(new UserByEmailResponse
        {
            Id = user.Id,
            Email = user.Email!,
            FirstName = user.FirstName,
            LastName = user.LastName,
            EmailConfirmed = user.EmailConfirmed,
            TwoFactorEnabled = user.TwoFactorEnabled,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt
        });
    }
}
