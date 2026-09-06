using EShop.Catalog.Application.Categories;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using EShop.Catalog.Application.Products.Queries.GetProductsById;
using EShop.Catalog.Domain.Entities;
using Mapster;

namespace EShop.Catalog.Application.Mapping;

/// <summary>
/// Mapster mapping configuration.
/// Implements IRegister so it is automatically discovered by TypeAdapterConfig.Scan().
/// </summary>
public class MappingConfig : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Product, ProductDto>()
            .Map(dest => dest.MainImageUrl,
                src => src.Images
                    .OrderBy(i => i.DisplayOrder)
                    .Select(i => i.Url)
                    .FirstOrDefault());

        // Canonical main-image pick: IsMain first, then DisplayOrder, then CreatedAt as a
        // tiebreaker — see catalog-images-variant-a-plan.md §3. Gallery order (Images[]) is
        // DisplayOrder then CreatedAt; IsMain is exposed as a flag so the client decides how
        // to surface it, rather than being folded into the gallery ordering itself.
        config.NewConfig<Product, ProductDetailsDto>()
            .Map(dest => dest.MainImageUrl,
                src => src.Images
                    .OrderByDescending(i => i.IsMain)
                    .ThenBy(i => i.DisplayOrder)
                    .ThenBy(i => i.CreatedAt)
                    .Select(i => i.Url)
                    .FirstOrDefault())
            .Map(dest => dest.Images,
                src => src.Images
                    .OrderBy(i => i.DisplayOrder)
                    .ThenBy(i => i.CreatedAt)
                    .Select(i => i.Adapt<ProductImageDto>())
                    .ToList())
            .Map(dest => dest.Attributes,
                src => src.Attributes.Select(a => a.Adapt<ProductAttributeDto>()).ToList());

        config.NewConfig<Category, CategoryDto>()
            .Map(dest => dest.ParentCategoryName, src => src.ParentCategory != null ? src.ParentCategory.Name : null)
            .Map(dest => dest.ChildCategories, src => src.ChildCategories.Select(cc => cc.Adapt<CategoryDto>()).ToList())
            .PreserveReference(true);
    }
}