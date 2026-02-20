using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Categories.Commands.DeleteCategory;

public class DeleteCategoryCommandHandler : IRequestHandler<DeleteCategoryCommand, Result>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteCategoryCommandHandler(
        ICategoryRepository categoryRepository,
        IProductRepository productRepository,
        IUnitOfWork unitOfWork)
    {
        _categoryRepository = categoryRepository;
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await _categoryRepository.GetById(request.Id, cancellationToken);
        if (category is null)
            return Result.Failure(new Error("Category.NotFound", $"Category with ID '{request.Id}' was not found."));

        if (category.ChildCategories.Count > 0)
            return Result.Failure(new Error("Category.HasChildren", "Cannot delete a category that has child categories. Remove children first."));

        var products = await _productRepository.GetByCategoryAsync(request.Id, cancellationToken);
        if (products.Any())
            return Result.Failure(new Error("Category.HasProducts", "Cannot delete a category that has products. Reassign or delete products first."));

        await _categoryRepository.DeleteAsync(category, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}