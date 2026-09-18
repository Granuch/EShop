using System.Text.RegularExpressions;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Domain.Exceptions;

namespace EShop.Catalog.Domain.Entities;

/// <summary>
/// Category aggregate root with hierarchical support.
///
/// <para>
/// <b>A category's parent is fixed at creation</b> (Catalog audit Stage 8, M12). There used to be a
/// public <c>SetParent</c> whose cycle check walked the in-memory <c>ParentCategory</c> navigation
/// and answered "no cycle" whenever that navigation was not loaded — and the repository loads one
/// level. No command ever re-parented a category, so the check guarded nothing, and it would have
/// passed a real cycle the day something did. A brand-new category cannot be anyone's ancestor, so
/// creation needs no cycle check. A future move operation must check the <i>persisted</i> chain (a
/// repository query), not the navigation.
/// </para>
///
/// <para>
/// <b>Deletion is soft</b> (M12): <see cref="Deactivate"/> clears <see cref="IsActive"/>, and the
/// global query filter hides the row from every query. The slug unique indexes are filtered on
/// <c>"IsActive"</c>, so a deleted category's slug is reusable — the category analogue of D2.
/// </para>
/// </summary>
public class Category : AggregateRoot<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string Slug { get; private set; } = string.Empty;
    public Guid? ParentCategoryId { get; private set; }
    public Category? ParentCategory { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    private readonly List<Category> _childCategories = new();
    public IReadOnlyCollection<Category> ChildCategories => _childCategories.AsReadOnly();

    private readonly List<Product> _products = new();
    public IReadOnlyCollection<Product> Products => _products.AsReadOnly();

    private Category() { }

    /// <param name="slug">
    /// Used as given (trimmed) when it has content; otherwise derived from the name. A name with no
    /// Latin letters or digits derives nothing, so the slug then falls back to one built from the id
    /// (M9) — it used to be stored as an empty string, and the second such root category collided
    /// on the unique index as a generic 409.
    /// </param>
    /// <param name="parent">The parent, or null for a root. Must not be deleted.</param>
    public static Category Create(
        string name,
        string? slug,
        Category? parent,
        string? description = null,
        int displayOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Category name is required.");

        if (parent is { IsActive: false })
            throw new DomainException("Cannot add a child to a deleted category.");

        if (displayOrder < 0)
            throw new DomainException("Display order cannot be negative.");

        var id = Guid.NewGuid();
        var trimmedName = name.Trim();

        var category = new Category
        {
            Id = id,
            Name = trimmedName,
            Slug = ResolveSlug(slug, trimmedName, id),
            Description = NormalizeDescription(description),
            DisplayOrder = displayOrder,
            ParentCategory = parent,
            ParentCategoryId = parent?.Id
        };

        // Keeps the aggregate consistent in memory. EF's relationship fixup does not add an entity a
        // collection already contains, so this does not produce a duplicate once tracked.
        parent?._childCategories.Add(category);

        return category;
    }

    /// <summary>
    /// Renames the category and, when given, changes its description and display order.
    ///
    /// <para>
    /// M10 — <paramref name="description"/> follows the BUG-09 rule: <c>null</c> (omitted) leaves
    /// the stored value alone, blank clears it, anything else replaces it. It used to be assigned
    /// unconditionally from a non-nullable command property that defaulted to <c>""</c>, so a
    /// <c>PUT</c> carrying only a name silently wiped the description and returned 204. The null
    /// check below is the fix — not redundant.
    /// </para>
    /// </summary>
    public void UpdateCategory(string name, string? description, int? displayOrder = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Category name cannot be empty.");

        if (displayOrder < 0)
            throw new DomainException("Display order cannot be negative.");

        Name = name.Trim();

        if (description is not null)
            Description = NormalizeDescription(description);

        if (displayOrder is { } order)
            DisplayOrder = order;
    }

    /// <summary>
    /// Soft-deletes the category. Idempotent. The handler refuses while live children or live
    /// products remain; this method cannot see products, so that check lives there.
    /// </summary>
    public void Deactivate() => IsActive = false;

    /// <summary>
    /// Brings a soft-deleted category back (Admin panel S5). Idempotent.
    /// </summary>
    /// <remarks>
    /// <b>Slug uniqueness is not checked here, and cannot be.</b> Both unique slug indexes are
    /// filtered on <c>"IsActive"</c>, so a deleted category's slug is free for another to take —
    /// and once taken, reactivating re-enters the filtered index and collides. The aggregate cannot
    /// see its siblings, so the pre-check lives in <c>RestoreCategoryCommandHandler</c> with the
    /// index as the race backstop, exactly as <c>Product.Restore</c> handles the SKU.
    /// <para>
    /// Restoring does <b>not</b> restore the parent: if the parent is itself still deleted the
    /// category comes back unreachable from the root list, which the handler refuses rather than
    /// silently cascading a reactivation nobody asked for.
    /// </para>
    /// </remarks>
    public void Restore()
    {
        if (IsActive)
            return;

        IsActive = true;
    }

    /// <summary>
    /// Used by the batch reorder (Admin panel S5) to place a category among its siblings.
    /// </summary>
    public void SetDisplayOrder(int displayOrder)
    {
        if (displayOrder < 0)
            throw new DomainException("Display order cannot be negative.");

        DisplayOrder = displayOrder;
    }

    /// <summary>
    /// Re-parents the category (Admin panel S5), or makes it a root when
    /// <paramref name="newParentId"/> is null.
    /// </summary>
    /// <param name="newParentAncestorIds">
    /// The <b>persisted</b> ancestor chain of the new parent, nearest first, as read from the
    /// database by the handler. Empty when the new parent is a root, and irrelevant when
    /// <paramref name="newParentId"/> is null.
    /// </param>
    /// <remarks>
    /// <para>
    /// <b>The chain is a parameter, not something this method walks.</b> A category's parent was
    /// fixed at creation from Stage 8 precisely because the old <c>SetParent</c>'s cycle check
    /// walked the in-memory <c>ParentCategory</c> navigation — which EF populates only as far as
    /// the query happened to <c>Include</c>, so it answered "no cycle" for a chain it could not
    /// see. Re-parenting is only safe with the check moved to a caller that can read the real
    /// chain; passing a navigation-derived list here reintroduces the original bug with the same
    /// shape and no failing test.
    /// </para>
    /// <para>
    /// Two cycles are possible and both are refused: making a category its own parent, and moving
    /// it under one of its own descendants (which shows up as this category appearing in the new
    /// parent's ancestor chain). Note the second is detected from the <i>parent's</i> ancestors
    /// rather than from this category's descendants — the aggregate can see neither, but the
    /// handler can read the former with one recursive query.
    /// </para>
    /// </remarks>
    public void MoveTo(Guid? newParentId, IReadOnlyCollection<Guid> newParentAncestorIds)
    {
        ArgumentNullException.ThrowIfNull(newParentAncestorIds);

        if (!IsActive)
            throw new DomainException("Cannot move a deleted category.");

        if (newParentId == Id)
            throw new DomainException("A category cannot be its own parent.");

        if (newParentId.HasValue && newParentAncestorIds.Contains(Id))
            throw new DomainException("Cannot move a category beneath one of its own descendants.");

        if (newParentId == ParentCategoryId)
            return;

        ParentCategoryId = newParentId;

        // Cleared so a stale navigation cannot be written back: the caller passed an id, and the
        // entity it points at is not loaded here. EF repopulates it on the next read.
        ParentCategory = null;
    }

    public static string GenerateSlug(string name)
    {
        var slug = name.ToLowerInvariant();
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", ""); // remove invalid chars
        slug = Regex.Replace(slug, @"\s+", "-"); // replace spaces with dash
        slug = Regex.Replace(slug, @"-+", "-"); // remove multiple dashes
        return slug.Trim('-'); // "Книги 2024" would otherwise yield "-2024"
    }

    /// <summary>
    /// The slug <see cref="Create"/> will give a category made from these inputs, or <c>null</c> when
    /// it will fall back to an id-based slug (which cannot collide, so needs no uniqueness check).
    ///
    /// <para>
    /// Public so a handler can check uniqueness <b>before</b> calling <see cref="Create"/>. Checking
    /// after is a trap: <see cref="Create"/> adds the new category to its (tracked) parent's
    /// children, and <c>TransactionBehavior</c> commits even when the handler returns a failure
    /// <c>Result</c> — so EF would discover the rejected category through the parent and insert it.
    /// </para>
    /// </summary>
    public static string? ResolveRequestedSlug(string? slug, string name)
    {
        if (!string.IsNullOrWhiteSpace(slug))
            return slug.Trim();

        var generated = GenerateSlug((name ?? string.Empty).Trim());
        return generated.Length > 0 ? generated : null;
    }

    private static string ResolveSlug(string? supplied, string name, Guid id)
        => ResolveRequestedSlug(supplied, name) ?? $"category-{id.ToString("N")[..8]}";

    private static string? NormalizeDescription(string? description)
        => string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
