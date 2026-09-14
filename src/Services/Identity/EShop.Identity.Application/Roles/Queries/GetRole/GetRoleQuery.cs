using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Identity.Application.Roles.Queries.GetRoles;

namespace EShop.Identity.Application.Roles.Queries.GetRole;

public record GetRoleQuery : IRequest<Result<RoleResponse>>
{
    public string RoleId { get; init; } = string.Empty;
}
