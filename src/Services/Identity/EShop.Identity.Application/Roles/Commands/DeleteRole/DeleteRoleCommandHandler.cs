using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Roles.Commands.DeleteRole;

public class DeleteRoleCommandHandler : IRequestHandler<DeleteRoleCommand, Result<Unit>>
{
    /// <summary>
    /// Roles the platform's own authorization policies depend on. Deleting either would strip
    /// every admin of their role with no way to grant it back through the API.
    /// </summary>
    private static readonly string[] SystemRoles = ["Admin", "User"];

    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ILogger<DeleteRoleCommandHandler> _logger;

    public DeleteRoleCommandHandler(
        RoleManager<ApplicationRole> roleManager,
        ILogger<DeleteRoleCommandHandler> logger)
    {
        _roleManager = roleManager;
        _logger = logger;
    }

    public async Task<Result<Unit>> Handle(DeleteRoleCommand request, CancellationToken cancellationToken)
    {
        var role = await _roleManager.FindByIdAsync(request.RoleId);

        if (role is null)
        {
            return Result<Unit>.Failure(RoleErrors.NotFound);
        }

        if (SystemRoles.Contains(role.Name, StringComparer.OrdinalIgnoreCase))
        {
            return Result<Unit>.Failure(RoleErrors.CannotDeleteSystemRole);
        }

        var result = await _roleManager.DeleteAsync(role);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            _logger.LogWarning("Failed to delete role. RoleId={RoleId}, Errors={Errors}", request.RoleId, errors);
            return Result<Unit>.Failure(RoleErrors.DeleteFailed(errors));
        }

        _logger.LogInformation("Role deleted: {RoleName}", role.Name);

        return Result<Unit>.Success(Unit.Value);
    }
}
