using EShop.BuildingBlocks.Application;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using EShop.Catalog.Domain.Interfaces;
using MapsterMapper;
using MediatR;

namespace EShop.Catalog.Application.Products.Queries.GetProductsById;

public sealed class GetProductByIdQueryHandler : IRequestHandler<GetProductByIdQuery, Result<ProductDto>>
{
    private readonly IProductRepository _productRepository;
    private readonly IMapper _mapper;

    public GetProductByIdQueryHandler(IProductRepository productRepository, IMapper mapper)
    {
        _productRepository = productRepository;
        _mapper = mapper;
    }

    public async Task<Result<ProductDto>> Handle(GetProductByIdQuery request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdReadOnlyAsync(request.ProductId, cancellationToken);

        if (product is null)
        {
            return Result<ProductDto>.Failure(new Error("Product.NotFound", $"Product with ID '{request.ProductId}' was not found."));
        }

        var dto = _mapper.Map<ProductDto>(product);
        return Result<ProductDto>.Success(dto);
    }
}