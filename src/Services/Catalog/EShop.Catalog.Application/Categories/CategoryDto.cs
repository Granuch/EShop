namespace EShop.Catalog.Application.Categories;

public record CategoryDto
{
    public Guid Id { get; set; }
    
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string Slug { get; set; } = string.Empty;

    public Guid? ParentCategoryId { get; set; }

    public string? ParentCategoryName { get; set; }

    public int DisplayOrder { get; set; }

    public bool IsActive { get; set; }

    public List<CategoryDto>? ChildCategories { get; set; } = new();
    
}