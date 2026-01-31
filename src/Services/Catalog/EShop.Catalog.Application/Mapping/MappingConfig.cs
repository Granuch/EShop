using EShop.Catalog.Application.Products.Queries.GetProducts;
using EShop.Catalog.Domain.Entities;

namespace EShop.Catalog.Application.Mapping;
using Mapster;

public class MappingConfig
{
    public static void RegisterMappings()
    {
        TypeAdapterConfig<Product, ProductDto>.NewConfig()
            // Pick first image by display order for MainImageUrl
            .Map(dest => dest.MainImageUrl,
                src => src.Images
                    .OrderBy(i => i.DisplayOrder)
                    .Select(i => i.Url)
                    .FirstOrDefault());
    }
}