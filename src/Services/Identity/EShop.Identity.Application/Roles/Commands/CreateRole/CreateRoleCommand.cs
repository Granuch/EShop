using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.Identity.Application.Roles.Queries.GetRoles;

namespace EShop.Identity.Application.Roles.Commands.CreateRole;

public record CreateRoleCommand : IRequest<Result<RoleResponse>>, ITransactionalCommand
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}
