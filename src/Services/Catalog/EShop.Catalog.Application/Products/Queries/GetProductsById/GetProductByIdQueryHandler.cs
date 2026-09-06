using EShop.BuildingBlocks.Application;
using EShop.Catalog.Domain.Interfaces;
using MapsterMapper;
using MediatR;

namespace EShop.Catalog.Application.Products.Queries.GetProductsById;

public sealed class GetProductByIdQueryHandler : IRequestHandler<GetProductByIdQuery, Result<ProductDetailsDto>>
{
    private readonly IProductRepository _productRepository;
    private readonly IMapper _mapper;

    public GetProductByIdQueryHandler(IProductRepository productRepository, IMapper mapper)
    {
        _productRepository = productRepository;
        _mapper = mapper;
    }

    public async Task<Result<ProductDetailsDto>> Handle(GetProductByIdQuery request, CancellationToken cancellationToken)
    {
        var product = await _productRepository.GetByIdReadOnlyAsync(request.ProductId, cancellationToken);

        if (product is null)
        {
            return Result<ProductDetailsDto>.Failure(new Error("Product.NotFound", $"Product with ID '{request.ProductId}' was not found."));
        }

        var dto = _mapper.Map<ProductDetailsDto>(product);
        return Result<ProductDetailsDto>.Success(dto);
    }
}