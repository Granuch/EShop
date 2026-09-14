using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Roles.Commands.AddUserToRole;

public class AddUserToRoleCommandHandler : IRequestHandler<AddUserToRoleCommand, Result<Unit>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ICachedUserRolesService _cachedUserRoles;
    private readonly ILogger<AddUserToRoleCommandHandler> _logger;

    public AddUserToRoleCommandHandler(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager,
        ICachedUserRolesService cachedUserRoles,
        ILogger<AddUserToRoleCommandHandler> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _cachedUserRoles = cachedUserRoles;
        _logger = logger;
    }

    public async Task<Result<Unit>> Handle(AddUserToRoleCommand request, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(request.UserId);

        if (user is null)
        {
            return Result<Unit>.Failure(RoleErrors.UserNotFound);
        }

        if (!await _roleManager.RoleExistsAsync(request.RoleName))
        {
            return Result<Unit>.Failure(RoleErrors.NotFound);
        }

        var result = await _userManager.AddToRoleAsync(user, request.RoleName);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            _logger.LogWarning(
                "Failed to add user to role. UserId={UserId}, RoleName={RoleName}, Errors={Errors}",
                request.UserId, request.RoleName, errors);
            return Result<Unit>.Failure(RoleErrors.AddUserFailed(errors));
        }

        // The roles cache is read when minting a token, so without this the change is
        // invisible for up to five minutes. Not ICacheInvalidatingCommand — see the command.
        await _cachedUserRoles.InvalidateRolesCacheAsync(request.UserId, cancellationToken);

        _logger.LogInformation("User {UserId} added to role {RoleName}", request.UserId, request.RoleName);

        return Result<Unit>.Success(Unit.Value);
    }
}
