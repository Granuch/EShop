using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Roles.Commands.UpdateRole;

public class UpdateRoleCommandHandler : IRequestHandler<UpdateRoleCommand, Result<Unit>>
{
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ILogger<UpdateRoleCommandHandler> _logger;

    public UpdateRoleCommandHandler(
        RoleManager<ApplicationRole> roleManager,
        ILogger<UpdateRoleCommandHandler> logger)
    {
        _roleManager = roleManager;
        _logger = logger;
    }

    public async Task<Result<Unit>> Handle(UpdateRoleCommand request, CancellationToken cancellationToken)
    {
        var role = await _roleManager.FindByIdAsync(request.RoleId);

        if (role is null)
        {
            return Result<Unit>.Failure(RoleErrors.NotFound);
        }

        role.Description = request.Description;

        var result = await _roleManager.UpdateAsync(role);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            _logger.LogWarning("Failed to update role. RoleId={RoleId}, Errors={Errors}", request.RoleId, errors);
            return Result<Unit>.Failure(RoleErrors.UpdateFailed(errors));
        }

        _logger.LogInformation("Role updated: {RoleName}", role.Name);

        return Result<Unit>.Success(Unit.Value);
    }
}
