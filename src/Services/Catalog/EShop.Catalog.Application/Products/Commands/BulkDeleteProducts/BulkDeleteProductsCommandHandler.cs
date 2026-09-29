using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Products.Bulk;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.BulkDeleteProducts;

public class BulkDeleteProductsCommandHandler : IRequestHandler<BulkDeleteProductsCommand, Result<BulkProductReport>>
{
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    public BulkDeleteProductsCommandHandler(IProductRepository productRepository, IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<BulkProductReport>> Handle(BulkDeleteProductsCommand request, CancellationToken cancellationToken)
    {
        var report = await BulkProductProcessor.ApplyAsync(
            request.ProductIds!,
            _productRepository,
            _unitOfWork,
            product =>
            {
                product.SoftDelete();
            },
            cancellationToken);

        return Result<BulkProductReport>.Success(report);
    }
}
