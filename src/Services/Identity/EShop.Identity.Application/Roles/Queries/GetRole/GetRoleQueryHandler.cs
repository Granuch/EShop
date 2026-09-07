using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Identity.Application.Roles.Queries.GetRoles;
using EShop.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace EShop.Identity.Application.Roles.Queries.GetRole;

public class GetRoleQueryHandler : IRequestHandler<GetRoleQuery, Result<RoleResponse>>
{
    private readonly RoleManager<ApplicationRole> _roleManager;

    public GetRoleQueryHandler(RoleManager<ApplicationRole> roleManager)
    {
        _roleManager = roleManager;
    }

    public async Task<Result<RoleResponse>> Handle(GetRoleQuery request, CancellationToken cancellationToken)
    {
        var role = await _roleManager.FindByIdAsync(request.RoleId);

        if (role is null)
        {
            return Result<RoleResponse>.Failure(RoleErrors.NotFound);
        }

        return Result<RoleResponse>.Success(new RoleResponse
        {
            Id = role.Id,
            Name = role.Name!,
            Description = role.Description
        });
    }
}
