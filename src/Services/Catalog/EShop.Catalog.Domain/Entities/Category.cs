using EShop.BuildingBlocks.Domain;

namespace EShop.Catalog.Domain.Entities;

/// <summary>
/// Category entity with hierarchical support
/// </summary>
public class Category : Entity<Guid>
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

    // TODO: Implement factory method Create()
    // public static Category Create(string name, string slug, Guid? parentCategoryId)

    // TODO: Implement AddChildCategory()
    // TODO: Implement slug generation from name
    // TODO: Add validation to prevent circular parent-child relationships
}
