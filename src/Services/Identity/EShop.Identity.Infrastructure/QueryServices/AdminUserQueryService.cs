using EShop.Identity.Domain.Interfaces;
using EShop.Identity.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EShop.Identity.Infrastructure.QueryServices;

/// <summary>
/// Read-side projections for the admin user screens (Admin panel S6).
/// </summary>
/// <remarks>
/// Every query here projects into a record and none of them materialise an
/// <c>ApplicationUser</c> — a list page reads a handful of columns, and loading tracked aggregates
/// to do that is how a user list becomes the slowest screen in the panel.
/// </remarks>
public class AdminUserQueryService : IAdminUserQueryService
{
    private readonly IdentityDbContext _dbContext;
    private readonly IUserRepository _userRepository;

    public AdminUserQueryService(IdentityDbContext dbContext, IUserRepository userRepository)
    {
        _dbContext = dbContext;
        _userRepository = userRepository;
    }

    public async Task<(IReadOnlyList<AdminUserRow> Rows, int TotalCount)> GetUsersAsync(
        AdminUserFilter filter,
        AdminUserSortBy sortBy,
        bool isDescending,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = ApplyFilter(_dbContext.Users.AsNoTracking(), filter);

        // Counted under the same filter and before paging: TotalCount must describe what the caller
        // can actually reach, or the pager offers pages that come back empty.
        var totalCount = await query.CountAsync(cancellationToken);

        // Id is the tiebreaker on every sort. CreatedAt, Email and LastLoginAt all repeat — and
        // LastLoginAt is null for every user who has never signed in — so without a unique final
        // key Postgres may order tied rows differently on each execution, and OFFSET paging could
        // repeat one row on two pages while never showing another.
        query = (sortBy, isDescending) switch
        {
            (AdminUserSortBy.Email, false) => query.OrderBy(u => u.Email).ThenBy(u => u.Id),
            (AdminUserSortBy.Email, true) => query.OrderByDescending(u => u.Email).ThenBy(u => u.Id),
            (AdminUserSortBy.LastLoginAt, false) => query.OrderBy(u => u.LastLoginAt).ThenBy(u => u.Id),
            (AdminUserSortBy.LastLoginAt, true) => query.OrderByDescending(u => u.LastLoginAt).ThenBy(u => u.Id),
            (_, true) => query.OrderByDescending(u => u.CreatedAt).ThenBy(u => u.Id),
            _ => query.OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
        };

        var rows = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminUserRow
            {
                Id = u.Id,
                Email = u.Email,
                UserName = u.UserName,
                FirstName = u.FirstName,
                LastName = u.LastName,
                EmailConfirmed = u.EmailConfirmed,
                TwoFactorEnabled = u.TwoFactorEnabled,
                IsActive = u.IsActive,
                IsDeleted = u.IsDeleted,
                DeletedAt = u.DeletedAt,
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt,
                LockoutEnd = u.LockoutEnd
            })
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
            return ([], totalCount);

        // One batch query for the whole page's roles rather than one per row — the N+1 this helper
        // exists to prevent.
        var rolesByUser = await _userRepository.GetRolesForUsersAsync(
            rows.Select(r => r.Id), cancellationToken);

        var withRoles = rows
            .Select(r => r with
            {
                Roles = rolesByUser.TryGetValue(r.Id, out var roles) ? roles : []
            })
            .ToList();

        return (withRoles, totalCount);
    }

    public async Task<AdminUserDetailRow?> GetUserDetailAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters so an admin can open a soft-deleted user's card — the whole point of
        // having one. Every other read path must keep treating a deleted user as absent.
        var row = await _dbContext.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(u => u.Id == userId)
            .Select(u => new AdminUserDetailRow
            {
                Id = u.Id,
                Email = u.Email,
                UserName = u.UserName,
                FirstName = u.FirstName,
                LastName = u.LastName,
                PhoneNumber = u.PhoneNumber,
                ProfilePictureUrl = u.ProfilePictureUrl,
                EmailConfirmed = u.EmailConfirmed,
                PhoneNumberConfirmed = u.PhoneNumberConfirmed,
                TwoFactorEnabled = u.TwoFactorEnabled,
                IsActive = u.IsActive,
                IsDeleted = u.IsDeleted,
                DeletedAt = u.DeletedAt,
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt,
                LastLoginIp = u.LastLoginIp,
                LockoutEnabled = u.LockoutEnabled,
                LockoutEnd = u.LockoutEnd,
                AccessFailedCount = u.AccessFailedCount,
                // Reduced to booleans on purpose: whether an external login is linked is what an
                // admin needs; the provider's subject identifier is a correlatable identity in
                // someone else's system and does not belong in this response.
                HasGoogleLogin = u.GoogleId != null,
                HasGitHubLogin = u.GitHubId != null
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
            return null;

        var rolesByUser = await _userRepository.GetRolesForUsersAsync([userId], cancellationToken);

        return row with
        {
            Roles = rolesByUser.TryGetValue(userId, out var roles) ? roles : []
        };
    }

    public async Task<AdminUserStats> GetUserStatsAsync(
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // IgnoreQueryFilters because Deleted is one of the numbers being reported: counting deleted
        // users under a filter that hides them would report zero, forever, with nothing failing.
        // Every other count then has to exclude them explicitly — which is why each predicate below
        // carries !u.IsDeleted rather than relying on the filter that is no longer there.
        var stats = await _dbContext.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .GroupBy(_ => 1)
            .Select(g => new AdminUserStats(
                g.Count(u => !u.IsDeleted),
                // The period bounds apply ONLY to this one number — the tile reads
                // "N total, M new in the period", so filtering the others by date would make every
                // figure mean something different from its label.
                g.Count(u => !u.IsDeleted
                    && (from == null || u.CreatedAt >= from)
                    && (to == null || u.CreatedAt <= to)),
                g.Count(u => !u.IsDeleted && u.IsActive),
                g.Count(u => !u.IsDeleted && u.LockoutEnd != null && u.LockoutEnd > now),
                g.Count(u => !u.IsDeleted && !u.EmailConfirmed),
                g.Count(u => u.IsDeleted)))
            .FirstOrDefaultAsync(cancellationToken);

        // GroupBy over an empty table yields no rows, not a row of zeros.
        return stats ?? new AdminUserStats(0, 0, 0, 0, 0, 0);
    }

    public async Task<IReadOnlyList<AdminUserSession>> GetUserSessionsAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // The projection names every field it returns, and TokenHash / ReplacedByTokenHash are not
        // among them. That is the security property of this method: a SELECT * shaped response, or
        // returning RefreshTokenEntity directly, would put a credential-equivalent hash on the wire
        // for every live session. Do not add them "for debugging".
        return await _dbContext.RefreshTokens
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .Select(t => new AdminUserSession(
                t.Id,
                t.CreatedAt,
                t.ExpiresAt,
                t.CreatedByIp,
                t.RevokedAt,
                t.RevokedByIp,
                t.RevokeReason,
                // IsActive is computed in SQL rather than read from the entity's property: that one
                // is unmapped (get-only, no backing field), so EF cannot translate it.
                t.RevokedAt == null && t.ExpiresAt > now))
            .ToListAsync(cancellationToken);
    }

    private IQueryable<Domain.Entities.ApplicationUser> ApplyFilter(
        IQueryable<Domain.Entities.ApplicationUser> query,
        AdminUserFilter filter)
    {
        // The user↔role join, projected once so the role filter below reads as a set membership
        // test rather than as a join that would multiply rows.
        var roleMembership = _dbContext.UserRoles
            .Join(_dbContext.Roles,
                ur => ur.RoleId,
                r => r.Id,
                (ur, r) => new { ur.UserId, NormalizedRoleName = r.NormalizedName });

        // Tri-state, matching the filter's documentation: true is the ONLY value that reveals
        // soft-deleted users, and it restricts the result to them. IgnoreQueryFilters applies to
        // the whole query rather than to the clauses after it, so the explicit predicate is what
        // scopes this — dropping it would widen the list to every user, live and deleted alike,
        // while still looking like a "deleted users" filter.
        if (filter.IsDeleted == true)
            query = query.IgnoreQueryFilters().Where(u => u.IsDeleted);

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            // Escape LIKE special characters so a search term cannot inject wildcards.
            var escaped = filter.Search.Trim()
                .Replace("\\", "\\\\")
                .Replace("%", "\\%")
                .Replace("_", "\\_");
            var term = $"%{escaped}%";

            query = query.Where(u =>
                EF.Functions.ILike(u.Email!, term, "\\") ||
                EF.Functions.ILike(u.UserName!, term, "\\") ||
                EF.Functions.ILike(u.FirstName, term, "\\") ||
                EF.Functions.ILike(u.LastName, term, "\\"));
        }

        if (!string.IsNullOrWhiteSpace(filter.Role))
        {
            var normalizedRole = filter.Role.Trim().ToUpperInvariant();

            // A subquery over the join table, because ApplicationUser has no UserRoles navigation —
            // Identity's join entity is mapped but not navigable from here. Matched on
            // NormalizedName, which is what ASP.NET Identity keeps uppercased for this kind of
            // lookup.
            //
            // Note a `Join` against the SAME filtered membership would behave identically, because
            // one role can match a user at most once; falsification confirmed that by swapping the
            // two and staying green. The set-membership form is kept because it stays correct if
            // this ever becomes "any of these roles", where a Join would return one row per
            // matching role and multiply the user — corrupting TotalCount as well as the page.
            var userIdsInRole = roleMembership
                .Where(m => m.NormalizedRoleName == normalizedRole)
                .Select(m => m.UserId);

            query = query.Where(u => userIdsInRole.Contains(u.Id));
        }

        if (filter.IsActive is { } isActive)
            query = query.Where(u => u.IsActive == isActive);

        if (filter.EmailConfirmed is { } emailConfirmed)
            query = query.Where(u => u.EmailConfirmed == emailConfirmed);

        if (filter.TwoFactorEnabled is { } twoFactorEnabled)
            query = query.Where(u => u.TwoFactorEnabled == twoFactorEnabled);

        if (filter.CreatedFrom is { } createdFrom)
            query = query.Where(u => u.CreatedAt >= createdFrom);

        if (filter.CreatedTo is { } createdTo)
            query = query.Where(u => u.CreatedAt <= createdTo);

        if (filter.LastLoginFrom is { } lastLoginFrom)
            query = query.Where(u => u.LastLoginAt != null && u.LastLoginAt >= lastLoginFrom);

        if (filter.LastLoginTo is { } lastLoginTo)
            query = query.Where(u => u.LastLoginAt != null && u.LastLoginAt <= lastLoginTo);

        return query;
    }
}
