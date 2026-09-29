using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Interfaces;
using MediatR;

namespace EShop.Identity.Application.Users.Queries.GetAdminUserDetails;

public class GetAdminUserDetailsQueryHandler
    : IRequestHandler<GetAdminUserDetailsQuery, Result<AdminUserDetailsDto>>
{
    private readonly IAdminUserQueryService _queryService;

    public GetAdminUserDetailsQueryHandler(IAdminUserQueryService queryService)
    {
        _queryService = queryService;
    }

    public async Task<Result<AdminUserDetailsDto>> Handle(
        GetAdminUserDetailsQuery request,
        CancellationToken cancellationToken)
    {
        var row = await _queryService.GetUserDetailAsync(request.UserId, cancellationToken);

        if (row is null)
            return Result<AdminUserDetailsDto>.Failure(new Error("User.NotFound", $"User with ID '{request.UserId}' was not found."));

        return Result<AdminUserDetailsDto>.Success(new AdminUserDetailsDto
        {
            Id = row.Id,
            Email = row.Email,
            UserName = row.UserName,
            FirstName = row.FirstName,
            LastName = row.LastName,
            PhoneNumber = row.PhoneNumber,
            ProfilePictureUrl = row.ProfilePictureUrl,
            EmailConfirmed = row.EmailConfirmed,
            PhoneNumberConfirmed = row.PhoneNumberConfirmed,
            TwoFactorEnabled = row.TwoFactorEnabled,
            IsActive = row.IsActive,
            IsDeleted = row.IsDeleted,
            DeletedAt = row.DeletedAt,
            CreatedAt = row.CreatedAt,
            LastLoginAt = row.LastLoginAt,
            LastLoginIp = row.LastLoginIp,
            LockoutEnabled = row.LockoutEnabled,
            LockoutEnd = row.LockoutEnd,
            IsLockedOut = row.LockoutEnd is { } lockoutEnd && lockoutEnd > DateTimeOffset.UtcNow,
            AccessFailedCount = row.AccessFailedCount,
            HasGoogleLogin = row.HasGoogleLogin,
            HasGitHubLogin = row.HasGitHubLogin,
            Roles = row.Roles
        });
    }
}
