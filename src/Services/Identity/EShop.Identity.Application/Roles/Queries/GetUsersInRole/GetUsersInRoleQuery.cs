using MediatR;
using EShop.BuildingBlocks.Application;

namespace EShop.Identity.Application.Roles.Queries.GetUsersInRole;

/// <summary>
/// Lists the members of a role. Bounded: the action this replaced returned every member of the
/// role in one response with no limit.
/// </summary>
public record GetUsersInRoleQuery : IRequest<Result<IReadOnlyList<UserInRoleResponse>>>
{
    public string RoleName { get; init; } = string.Empty;
    public int PageSize { get; init; } = 50;
    public int Page { get; init; } = 1;
}

public record UserInRoleResponse
{
    public string Id { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
}
