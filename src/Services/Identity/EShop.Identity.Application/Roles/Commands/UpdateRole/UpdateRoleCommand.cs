using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Behaviors;

namespace EShop.Identity.Application.Roles.Commands.UpdateRole;

public record UpdateRoleCommand : IRequest<Result<Unit>>, ITransactionalCommand
{
    public string RoleId { get; init; } = string.Empty;
    public string? Description { get; init; }
}
