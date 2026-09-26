using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EShop.Identity.Application.Roles.Queries.GetRoles;

public class GetRolesQueryHandler : IRequestHandler<GetRolesQuery, Result<PagedResult<RoleResponse>>>
{
    private readonly RoleManager<ApplicationRole> _roleManager;

    public GetRolesQueryHandler(RoleManager<ApplicationRole> roleManager)
    {
        _roleManager = roleManager;
    }

    public async Task<Result<PagedResult<RoleResponse>>> Handle(
        GetRolesQuery request,
        CancellationToken cancellationToken)
    {
        var pageNumber = request.EffectivePageNumber();
        var pageSize = request.EffectivePageSize();

        var totalCount = await _roleManager.Roles.CountAsync(cancellationToken);

        var roles = await _roleManager.Roles
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new RoleResponse
            {
                Id = r.Id,
                Name = r.Name!,
                Description = r.Description
            })
            .ToListAsync(cancellationToken);

        return Result<PagedResult<RoleResponse>>.Success(
            PagedResult<RoleResponse>.Create(roles, pageNumber, pageSize, totalCount));
    }
}
