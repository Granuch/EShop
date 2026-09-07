using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace EShop.Identity.Application.Roles.Queries.GetUsersInRole;

public class GetUsersInRoleQueryHandler
    : IRequestHandler<GetUsersInRoleQuery, Result<IReadOnlyList<UserInRoleResponse>>>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;

    public GetUsersInRoleQueryHandler(
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<Result<IReadOnlyList<UserInRoleResponse>>> Handle(
        GetUsersInRoleQuery request,
        CancellationToken cancellationToken)
    {
        // The old action skipped this check, so an unknown role name returned 200 with an empty
        // list — indistinguishable from a real role that happens to have no members.
        if (!await _roleManager.RoleExistsAsync(request.RoleName))
        {
            return Result<IReadOnlyList<UserInRoleResponse>>.Failure(RoleErrors.NotFound);
        }

        // GetUsersInRoleAsync materialises the whole membership, so the paging below is in
        // memory. Acceptable at this size and unchanged from before; a role large enough for
        // that to matter needs a projection query, not a bigger page.
        var users = await _userManager.GetUsersInRoleAsync(request.RoleName);

        var page = users
            .OrderBy(u => u.Email)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(u => new UserInRoleResponse
            {
                Id = u.Id,
                Email = u.Email!,
                FirstName = u.FirstName,
                LastName = u.LastName
            })
            .ToList();

        return Result<IReadOnlyList<UserInRoleResponse>>.Success(page);
    }
}
