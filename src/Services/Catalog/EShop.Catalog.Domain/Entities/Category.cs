using System.Text.RegularExpressions;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Domain.Exceptions;

namespace EShop.Catalog.Domain.Entities;

/// <summary>
/// Category aggregate root with hierarchical support
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
    
    public static Category Create(string name, string? slug, Guid? parentCategoryId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Category name is required.");
        
        var finalSlug = (slug ?? GenerateSlug(name)).Trim();
        
        var newCategory = new Category
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Slug = finalSlug,
            ParentCategoryId = parentCategoryId
        };

        return newCategory;
    }
    
    public void AddChildCategory(string name, string? slug = null)
    {
        if (!IsActive)
            throw new DomainException("Cannot add a child to an inactive category.");

        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Child category name cannot be empty.");

        string newSlug = (slug ?? GenerateSlug(name)).Trim();
        
        var juniorCategory = Category.Create(name, newSlug, Id);
        
        _childCategories.Add(juniorCategory);
    }

    public void UpdateCategory(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Category name cannot be empty.");

        Name = name.Trim();
        Description = description?.Trim();
    }

    public static string GenerateSlug(string name)
    {
        var slug = name.ToLowerInvariant();
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", ""); // remove invalid chars
        slug = Regex.Replace(slug, @"\s+", "-"); // replace spaces with dash
        slug = Regex.Replace(slug, @"-+", "-"); // remove multiple dashes
        return slug;
    }
    // TODO: Add validation to prevent circular parent-child relationships
    private bool IsAncestorOf(Guid categoryId)
    {
        if (ParentCategoryId == null)
            return false;

        if (ParentCategoryId == categoryId)
            return true;

        return ParentCategory?.IsAncestorOf(categoryId) ?? false;
    }
}
