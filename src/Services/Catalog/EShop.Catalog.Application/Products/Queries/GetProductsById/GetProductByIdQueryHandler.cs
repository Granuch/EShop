using EShop.BuildingBlocks.Application;
using EShop.Catalog.Domain.Entities;
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

        // D1 / H5a. An unpublished product is 404 to a public caller, not 403: its existence is not
        // something the public catalog should confirm. Admins (IncludeUnpublished, set from the
        // caller's role at the endpoint) get the real product so they can preview before publishing
        // — without that, a product would be invisible to the very person who just created it.
        if (product is null || (!request.IncludeUnpublished && product.Status != ProductStatus.Active))
        {
            return Result<ProductDetailsDto>.Failure(new Error("Product.NotFound", $"Product with ID '{request.ProductId}' was not found."));
        }

        var dto = _mapper.Map<ProductDetailsDto>(product);
        return Result<ProductDetailsDto>.Success(dto);
    }
}