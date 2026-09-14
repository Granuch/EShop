using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Categories.Commands.DeleteCategory;

/// <summary>
/// M12 (Catalog audit Stage 8): delete is a <b>soft</b> delete. It used to be a hard
/// <c>Categories.Remove</c> — irreversible, the opposite of <c>Product.SoftDelete</c>, and, because
/// Product → Category is <c>ON DELETE CASCADE</c>, it silently took every soft-deleted product in the
/// category with it. The <c>IsActive</c> query filter had existed all along with nothing able to
/// set it false.
/// </summary>
public class DeleteCategoryCommandHandler : IRequestHandler<DeleteCategoryCommand, Result>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly IProductRepository _productRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheInvalidationContext _cacheInvalidationContext;

    public DeleteCategoryCommandHandler(
        ICategoryRepository categoryRepository,
        IProductRepository productRepository,
        IUnitOfWork unitOfWork,
        ICacheInvalidationContext cacheInvalidationContext)
    {
        _categoryRepository = categoryRepository;
        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
        _cacheInvalidationContext = cacheInvalidationContext;
    }

    public async Task<Result> Handle(DeleteCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await _categoryRepository.GetById(request.Id, cancellationToken);
        if (category is null)
            return Result.Failure(new Error("Category.NotFound", $"Category with ID '{request.Id}' was not found."));

        // The included children pass through the same IsActive filter, so only live children
        // block — a category whose children were all deleted can itself be deleted.
        if (category.ChildCategories.Count > 0)
            return Result.Failure(new Error("Category.HasChildren", "Cannot delete a category that has child categories. Remove children first."));

        // M17. An existence question, asked as one: this used to materialise up to 200 full
        // products and call .Any() on the list.
        if (await _productRepository.AnyInCategoryAsync(request.Id, cancellationToken))
            return Result.Failure(new Error("Category.HasProducts", "Cannot delete a category that has products. Reassign or delete products first."));

        category.Deactivate();
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // M8. The parent's cached detail still lists this category until its entry goes.
        _cacheInvalidationContext.AddKeys(CategoryCacheKeys.RelativesOf(category));

        return Result.Success();
    }
}
