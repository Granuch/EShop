using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using FluentValidation;

namespace EShop.Identity.Application.Roles.Queries.GetUsersInRole;

/// <summary>
/// Lists the members of a role, one page at a time — see <see cref="RolePaging"/>. The action this
/// replaced returned every member of the role in one response with no limit.
/// </summary>
public record GetUsersInRoleQuery : IRequest<Result<PagedResult<UserInRoleResponse>>>, IRolePageQuery
{
    public string RoleName { get; init; } = string.Empty;
    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }
}

public class GetUsersInRoleQueryValidator : AbstractValidator<GetUsersInRoleQuery>
{
    public GetUsersInRoleQueryValidator() => RolePaging.Apply(this);
}

public record UserInRoleResponse
{
    public string Id { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
}
