using EShop.BuildingBlocks.Application;
using MediatR;

namespace EShop.Identity.Application.Roles.Queries.GetRoles;

/// <summary>
/// Lists roles. Bounded by <see cref="PageSize"/> — the controller action this replaced returned
/// an unmaterialised <c>IQueryable</c> over the whole table, so the query executed inside the
/// serializer while the response was being written and had no row limit at all.
/// </summary>
public record GetRolesQuery : IRequest<Result<IReadOnlyList<RoleResponse>>>
{
    public int PageSize { get; init; } = 50;
    public int Page { get; init; } = 1;
}

public record RoleResponse
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}
