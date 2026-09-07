using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Identity.Application.Roles.Queries.GetRoles;
using EShop.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace EShop.Identity.Application.Roles.Commands.CreateRole;

public class CreateRoleCommandHandler : IRequestHandler<CreateRoleCommand, Result<RoleResponse>>
{
    private readonly RoleManager<ApplicationRole> _roleManager;
    private readonly ILogger<CreateRoleCommandHandler> _logger;

    public CreateRoleCommandHandler(
        RoleManager<ApplicationRole> roleManager,
        ILogger<CreateRoleCommandHandler> logger)
    {
        _roleManager = roleManager;
        _logger = logger;
    }

    public async Task<Result<RoleResponse>> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        if (await _roleManager.RoleExistsAsync(request.Name))
        {
            return Result<RoleResponse>.Failure(RoleErrors.AlreadyExists);
        }

        var role = new ApplicationRole
        {
            Name = request.Name,
            Description = request.Description
        };

        var result = await _roleManager.CreateAsync(role);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            _logger.LogWarning("Failed to create role. Name={RoleName}, Errors={Errors}", request.Name, errors);
            return Result<RoleResponse>.Failure(RoleErrors.CreateFailed(errors));
        }

        _logger.LogInformation("Role created: {RoleName}", role.Name);

        return Result<RoleResponse>.Success(new RoleResponse
        {
            Id = role.Id,
            Name = role.Name!,
            Description = role.Description
        });
    }
}
