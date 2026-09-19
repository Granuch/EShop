using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;
using EShop.Identity.Domain.Interfaces;
using MediatR;

namespace EShop.Identity.Application.Users.Queries.GetAdminUsers;

public class GetAdminUsersQueryHandler
    : IRequestHandler<GetAdminUsersQuery, Result<PagedResult<AdminUserDto>>>
{
    private readonly IAdminUserQueryService _queryService;

    public GetAdminUsersQueryHandler(IAdminUserQueryService queryService)
    {
        _queryService = queryService;
    }

    public async Task<Result<PagedResult<AdminUserDto>>> Handle(
        GetAdminUsersQuery request,
        CancellationToken cancellationToken)
    {
        var filter = new AdminUserFilter(
            request.Search,
            request.Role,
            request.IsActive,
            request.IsDeleted,
            request.EmailConfirmed,
            request.TwoFactorEnabled,
            request.CreatedFrom,
            request.CreatedTo,
            request.LastLoginFrom,
            request.LastLoginTo);

        var (rows, totalCount) = await _queryService.GetUsersAsync(
            filter,
            request.EffectiveSortBy,
            request.EffectiveIsDescending,
            request.EffectivePageNumber,
            request.EffectivePageSize,
            cancellationToken);

        var now = DateTimeOffset.UtcNow;

        var items = rows.Select(r => new AdminUserDto
        {
            Id = r.Id,
            Email = r.Email,
            UserName = r.UserName,
            FirstName = r.FirstName,
            LastName = r.LastName,
            EmailConfirmed = r.EmailConfirmed,
            TwoFactorEnabled = r.TwoFactorEnabled,
            IsActive = r.IsActive,
            IsDeleted = r.IsDeleted,
            DeletedAt = r.DeletedAt,
            CreatedAt = r.CreatedAt,
            LastLoginAt = r.LastLoginAt,
            LockoutEnd = r.LockoutEnd,
            // Computed here rather than in SQL: "now" is a single instant for the whole page, so
            // two rows cannot disagree about whether the lockout boundary has passed.
            IsLockedOut = r.LockoutEnd is { } lockoutEnd && lockoutEnd > now,
            Roles = r.Roles
        }).ToList();

        var paged = PagedResult<AdminUserDto>.Create(
            items, request.EffectivePageNumber, request.EffectivePageSize, totalCount);

        return Result<PagedResult<AdminUserDto>>.Success(paged);
    }
}
