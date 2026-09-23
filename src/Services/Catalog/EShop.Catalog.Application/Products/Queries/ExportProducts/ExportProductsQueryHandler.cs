using EShop.BuildingBlocks.Application;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Application.Products.Queries.GetProducts;
using MediatR;

namespace EShop.Catalog.Application.Products.Queries.ExportProducts;

public class ExportProductsQueryHandler : IRequestHandler<ExportProductsQuery, Result<IReadOnlyList<ProductDto>>>
{
    private readonly IProductQueryService _productQueryService;

    public ExportProductsQueryHandler(IProductQueryService productQueryService)
    {
        _productQueryService = productQueryService;
    }

    public async Task<Result<IReadOnlyList<ProductDto>>> Handle(ExportProductsQuery request, CancellationToken cancellationToken)
    {
        // Drafts included: the endpoint is admin-only. See ExportProductsQuery.
        var (products, matching) = await _productQueryService.GetProductsForExportAsync(
            request.ToFilter(includeUnpublished: true),
            request.EffectiveSortBy,
            request.EffectiveIsDescending,
            ExportProductsQuery.MaxRows,
            cancellationToken);

        if (matching > ExportProductsQuery.MaxRows)
        {
            return Result<IReadOnlyList<ProductDto>>.Failure(new Error(
                "Products.ExportTooLarge",
                $"{matching} products match; an export is limited to {ExportProductsQuery.MaxRows}. "
                + "Narrow the filters."));
        }

        return Result<IReadOnlyList<ProductDto>>.Success(products);
    }
}
