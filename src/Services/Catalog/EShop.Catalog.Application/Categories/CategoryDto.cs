namespace EShop.Catalog.Application.Categories;

public record CategoryDto
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string Slug { get; init; } = string.Empty;

    public Guid? ParentCategoryId { get; init; }

    public string? ParentCategoryName { get; init; }

    public int DisplayOrder { get; init; }

    public bool IsActive { get; init; }

    public List<CategoryDto>? ChildCategories { get; init; } = [];
}