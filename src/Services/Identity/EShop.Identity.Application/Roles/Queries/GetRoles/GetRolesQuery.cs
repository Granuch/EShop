using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using FluentValidation;
using MediatR;

namespace EShop.Identity.Application.Roles.Queries.GetRoles;

/// <summary>
/// Lists roles, one page at a time — see <see cref="RolePaging"/>. The controller action this
/// replaced returned an unmaterialised <c>IQueryable</c> over the whole table, so the query executed
/// inside the serializer while the response was being written and had no row limit at all.
/// </summary>
public record GetRolesQuery : IRequest<Result<PagedResult<RoleResponse>>>, IRolePageQuery
{
    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }
}

public class GetRolesQueryValidator : AbstractValidator<GetRolesQuery>
{
    public GetRolesQueryValidator() => RolePaging.Apply(this);
}

public record RoleResponse
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}
