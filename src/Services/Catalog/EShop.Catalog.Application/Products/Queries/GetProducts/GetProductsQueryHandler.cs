using MediatR;
using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Pagination;

namespace EShop.Catalog.Application.Products.Queries.GetProducts;

/// <summary>
/// Handler for getting products with filtering and pagination
/// </summary>
public class GetProductsQueryHandler : IRequestHandler<GetProductsQuery, Result<PagedResult<ProductDto>>>
{
    // TODO: Inject IProductRepository, IMapper/Mapster, IDistributedCache
    // private readonly IProductRepository _productRepository;
    // private readonly IDistributedCache _cache;

    public async Task<Result<PagedResult<ProductDto>>> Handle(GetProductsQuery request, CancellationToken cancellationToken)
    {
        // TODO: Try to get from cache first (cache key: "products:{categoryId}:{page}:{pageSize}")
        // TODO: Build query with filters (category, search, price range)
        // TODO: Apply sorting
        // TODO: Apply pagination
        // TODO: Map to ProductDto
        // TODO: Cache result for 5 minutes
        // TODO: Return paged result
        throw new NotImplementedException();
    }
}
