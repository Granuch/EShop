using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Products.Bulk;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.BulkPublishProducts;

public class BulkPublishProductsCommandHandler : IRequestHandler<BulkPublishProductsCommand, Result<BulkProductReport>>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    public BulkPublishProductsCommandHandler(IProductRepository productRepository, IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<BulkProductReport>> Handle(BulkPublishProductsCommand request, CancellationToken cancellationToken)
    {
        var report = await BulkProductProcessor.ApplyAsync(
            request.ProductIds!,
            _productRepository,
            _unitOfWork,
            product =>
            {
                // Idempotent like the single endpoint: Product.Publish refuses a non-draft, and an Active product is
                // already where this request wants it.
                if (product.Status != ProductStatus.Active)
                    product.Publish();
            },
            cancellationToken);

        return Result<BulkProductReport>.Success(report);
    }
}
