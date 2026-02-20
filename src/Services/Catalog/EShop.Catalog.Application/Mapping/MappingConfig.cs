using EShop.Catalog.Application.Categories;
using EShop.Catalog.Application.Products.Queries.GetProducts;
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

        config.NewConfig<Category, CategoryDto>()
            .Map(dest => dest.ParentCategoryName, src => src.ParentCategory != null ? src.ParentCategory.Name : null)
            .Map(dest => dest.ChildCategories, src => src.ChildCategories.Select(cc => cc.Adapt<CategoryDto>()).ToList())
            .PreserveReference(true);
    }
}