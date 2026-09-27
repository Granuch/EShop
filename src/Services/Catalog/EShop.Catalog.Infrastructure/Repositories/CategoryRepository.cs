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
    /// F-37 (frontend-contracts R5): the whole table in one query, for <c>CategoryTree</c> to
    /// assemble at any depth. The <c>Include</c>/<c>ThenInclude</c> form this replaced loaded exactly
    /// two levels below the roots and <c>Take(100)</c> roots, both silently.
    /// </summary>
    /// <remarks>
    /// M11. Ordered by DisplayOrder, then Name, then Id — every level's siblings keep this order when
    /// grouped by parent. DisplayOrder alone was the only key once, and every category had 0, so the
    /// order was whatever Postgres returned, cached for ten minutes.
    /// </remarks>
    public async Task<List<Category>> GetAllAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var query = _context.Categories.AsNoTracking();

        // A4. Lifting the filter for the whole read is what makes a deactivated category appear at
        // every depth — a live root's deactivated children as much as a deactivated root.
        if (includeInactive)
            query = query.IgnoreQueryFilters();

        return await query
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> SlugExistsAsync(Guid? parentCategoryId, string slug, CancellationToken cancellationToken = default)
        => _context.Categories
            .AsNoTracking()
            .AnyAsync(c => c.ParentCategoryId == parentCategoryId && c.Slug == slug, cancellationToken);
}
