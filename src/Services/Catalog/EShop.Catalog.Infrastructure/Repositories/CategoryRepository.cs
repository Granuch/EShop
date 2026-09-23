using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using EShop.Catalog.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EShop.Catalog.Infrastructure.Repositories;

public class CategoryRepository : ICategoryRepository
{
    private readonly CatalogDbContext _context;

    public CategoryRepository(CatalogDbContext context)
    {
        _context = context;
    }

    public async Task<Category?> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Categories
            .Include(c => c.ParentCategory)
            .Include(c => c.ChildCategories.OrderBy(child => child.DisplayOrder).ThenBy(child => child.Name))
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<HashSet<Guid>> GetExistingIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken = default)
    {
        // Under the IsActive global filter, like GetById: a deactivated category is as absent here as it is to a single
        // create, so an import cannot put products into a category nobody can see.
        var existing = await _context.Categories
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        return existing.ToHashSet();
    }

    public async Task<Category?> GetByIdIncludingInactiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Categories
            .IgnoreQueryFilters()
            .Include(c => c.ParentCategory)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    /// <summary>
    /// A recursive CTE rather than a loop of round trips, and deliberately not
    /// <c>IgnoreQueryFilters</c>-sensitive: it reads the raw table, so a deactivated ancestor still
    /// appears. A cycle through a soft-deleted category is still a cycle.
    /// </summary>
    public async Task<List<Guid>> GetAncestorIdsAsync(Guid categoryId, CancellationToken cancellationToken = default)
    {
        // Depth is carried so the result can be ordered nearest-parent-first, which is the order
        // Category.MoveTo's documentation promises. The self row is seeded at depth 0 and excluded
        // below, because a category is not its own ancestor.
        //
        // The depth guard is a safety net, not a correctness condition: it cannot terminate a cycle
        // that already exists in the data (the CTE would loop forever without it), which is exactly
        // the state this whole feature is meant to prevent ever reaching the database.
        var sql = """
            WITH RECURSIVE ancestors AS (
                SELECT c."Id", c."ParentCategoryId", 0 AS depth
                FROM "Categories" c
                WHERE c."Id" = {0}
                UNION ALL
                SELECT p."Id", p."ParentCategoryId", a.depth + 1
                FROM "Categories" p
                JOIN ancestors a ON p."Id" = a."ParentCategoryId"
                WHERE a.depth < 100
            )
            SELECT a."Id" AS "Value"
            FROM ancestors a
            WHERE a.depth > 0
            ORDER BY a.depth
            """;
        // The "Value" alias is required, not cosmetic: EF Core's SqlQueryRaw<T> for a scalar type
        // binds a single column named exactly Value, and without the alias it fails at runtime with
        // a message about the column, not about the alias.

        return await _context.Database
            .SqlQueryRaw<Guid>(sql, categoryId)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Category>> GetSiblingsAsync(Guid? parentCategoryId, CancellationToken cancellationToken = default)
    {
        return await _context.Categories
            .Where(c => c.ParentCategoryId == parentCategoryId)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Category category, CancellationToken cancellationToken = default)
    {
        await _context.Categories.AddAsync(category, cancellationToken);
    }

    public Task UpdateAsync(Category category, CancellationToken cancellationToken = default)
    {
        // No explicit Update() — entity is change-tracked.
        return Task.CompletedTask;
    }

    /// <summary>
    /// M11. Ordered by DisplayOrder, then Name, then Id. DisplayOrder alone was the only key, and
    /// every category had 0 (create could not set it), so the root order was whatever Postgres
    /// returned — and that order was then cached for ten minutes.
    /// </summary>
    public async Task<List<Category>> GetRootCategories(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var query = _context.Categories.AsQueryable();

        // A4. IgnoreQueryFilters applies to the whole query including the Includes below, which is
        // what makes deactivated CHILDREN appear too — filtering only the roots would show a
        // deactivated root's live children while hiding a live root's deactivated ones.
        if (includeInactive)
            query = query.IgnoreQueryFilters();

        return await query
            .Where(c => c.ParentCategoryId == null)
            .Include(c => c.ChildCategories.OrderBy(child => child.DisplayOrder).ThenBy(child => child.Name))
                .ThenInclude(c => c.ChildCategories.OrderBy(grandchild => grandchild.DisplayOrder).ThenBy(grandchild => grandchild.Name))
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ThenBy(c => c.Id)
            .Take(100)
            .AsSplitQuery()
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public Task<bool> SlugExistsAsync(Guid? parentCategoryId, string slug, CancellationToken cancellationToken = default)
        => _context.Categories
            .AsNoTracking()
            .AnyAsync(c => c.ParentCategoryId == parentCategoryId && c.Slug == slug, cancellationToken);
}
