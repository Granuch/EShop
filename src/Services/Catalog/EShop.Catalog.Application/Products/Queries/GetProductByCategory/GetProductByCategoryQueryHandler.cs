using EShop.BuildingBlocks.Application;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using EShop.Catalog.Domain.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace EShop.Catalog.Application.Products.Queries.GetProductByCategory;

public sealed class GetProductByCategoryQueryHandler : IRequestHandler<GetProductByCategoryQuery, Result<List<ProductDto>>>
{
    private readonly IProductRepository _productRepository;

    public GetProductByCategoryQueryHandler(IProductRepository productRepository)
    {
        _productRepository = productRepository;
    }

    public async Task<Result<List<ProductDto>>> Handle(GetProductByCategoryQuery request, CancellationToken cancellationToken)
    {
        var dtos = await _productRepository.Query()
            .Where(p => p.CategoryId == request.CategoryId)
            .OrderBy(p => p.Name)
            .Take(200)
            .Select(p => new ProductDto
            {
                Id = p.Id,
                Name = p.Name,
                Description = p.Description,
                Sku = p.Sku,
                Price = p.Price,
                DiscountPrice = p.DiscountPrice,
                StockQuantity = p.StockQuantity,
                Status = p.Status,
                CategoryId = p.CategoryId,
                MainImageUrl = p.Images
                    .OrderBy(i => i.DisplayOrder)
                    .Select(i => i.Url)
                    .FirstOrDefault(),
                CreatedAt = p.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Result<List<ProductDto>>.Success(dtos);
    }
}