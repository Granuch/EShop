using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Products.Bulk;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.BulkUnpublishProducts;

public class BulkUnpublishProductsCommandHandler : IRequestHandler<BulkUnpublishProductsCommand, Result<BulkProductReport>>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    public BulkUnpublishProductsCommandHandler(IProductRepository productRepository, IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<BulkProductReport>> Handle(BulkUnpublishProductsCommand request, CancellationToken cancellationToken)
    {
        var report = await BulkProductProcessor.ApplyAsync(
            request.ProductIds!,
            _productRepository,
            _unitOfWork,
            product =>
            {
                // Idempotent like the single endpoint: a draft is already unpublished.
                if (product.Status != ProductStatus.Draft)
                    product.Unpublish();
            },
            cancellationToken);

        return Result<BulkProductReport>.Success(report);
    }
}
