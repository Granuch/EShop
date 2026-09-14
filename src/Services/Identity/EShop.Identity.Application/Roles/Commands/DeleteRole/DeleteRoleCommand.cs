using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;

namespace EShop.Identity.Application.Roles.Commands.DeleteRole;

public record DeleteRoleCommand : IRequest<Result<Unit>>, ITransactionalCommand
{
    public string RoleId { get; init; } = string.Empty;
}
