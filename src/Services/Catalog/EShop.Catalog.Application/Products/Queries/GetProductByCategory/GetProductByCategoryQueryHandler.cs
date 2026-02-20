using EShop.BuildingBlocks.Application;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using MediatR;

namespace EShop.Catalog.Application.Products.Queries.GetProductByCategory;

public sealed class GetProductByCategoryQueryHandler : IRequestHandler<GetProductByCategoryQuery, Result<List<ProductDto>>>
{
    private readonly IProductQueryService _productQueryService;

    public GetProductByCategoryQueryHandler(IProductQueryService productQueryService)
    {
        _productQueryService = productQueryService;
    }

    public async Task<Result<List<ProductDto>>> Handle(GetProductByCategoryQuery request, CancellationToken cancellationToken)
    {
        var dtos = await _productQueryService.GetProductsByCategoryAsync(
            request.CategoryId,
            maxResults: 200,
            cancellationToken);

        return Result<List<ProductDto>>.Success(dtos);
    }
}