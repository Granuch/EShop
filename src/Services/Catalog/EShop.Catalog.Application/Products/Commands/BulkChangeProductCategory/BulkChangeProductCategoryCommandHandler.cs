using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Products.Bulk;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Products.Commands.BulkChangeProductCategory;

public class BulkChangeProductCategoryCommandHandler : IRequestHandler<BulkChangeProductCategoryCommand, Result<BulkProductReport>>
{
    private readonly IProductRepository _productRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IUnitOfWork _unitOfWork;

    public BulkChangeProductCategoryCommandHandler(
        IProductRepository productRepository,
        ICategoryRepository categoryRepository,
        IUnitOfWork unitOfWork)
    {
        _productRepository = productRepository;
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<BulkProductReport>> Handle(BulkChangeProductCategoryCommand request, CancellationToken cancellationToken)
    {
        // Before anything is loaded or changed: TransactionBehavior commits on a failure Result, so this refusal must
        // leave nothing behind to commit. The existence query also tracks nothing, so the category cannot be attached to
        // the moved products and written back.
        var existing = await _categoryRepository.GetExistingIdsAsync([request.CategoryId], cancellationToken);
        if (!existing.Contains(request.CategoryId))
        {
            return Result<BulkProductReport>.Failure(new Error(
                "Category.NotFound", $"Category with ID '{request.CategoryId}' was not found."));
        }

        var report = await BulkProductProcessor.ApplyAsync(
            request.ProductIds!,
            _productRepository,
            _unitOfWork,
            product => product.ChangeCategory(request.CategoryId),
            cancellationToken);

        return Result<BulkProductReport>.Success(report);
    }
}
