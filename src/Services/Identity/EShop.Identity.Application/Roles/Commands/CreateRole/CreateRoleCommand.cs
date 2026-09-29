using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Auditing;
using EShop.BuildingBlocks.Application.Behaviors;
using EShop.Identity.Application.Roles.Queries.GetRoles;

namespace EShop.Identity.Application.Roles.Commands.CreateRole;

public record CreateRoleCommand : IRequest<Result<RoleResponse>>, ITransactionalCommand, IAuditedCommand
{
    string IAuditedCommand.AuditEntityType => "Role";

    string? IAuditedCommand.AuditEntityId => null;

    string? IAuditedCommand.AuditEntityIdFromResult(object? value) => (value as RoleResponse)?.Id;

    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}
