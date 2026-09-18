using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Categories.Commands.RestoreCategory;

public class RestoreCategoryCommandHandler : IRequestHandler<RestoreCategoryCommand, Result>
{
    private readonly ICategoryRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheInvalidationContext _cacheInvalidationContext;

    public RestoreCategoryCommandHandler(
        ICategoryRepository repository,
        IUnitOfWork unitOfWork,
        ICacheInvalidationContext cacheInvalidationContext)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _cacheInvalidationContext = cacheInvalidationContext;
    }

    public async Task<Result> Handle(RestoreCategoryCommand request, CancellationToken cancellationToken)
    {
        // GetById runs under the c.IsActive global filter, so it answers null for exactly the
        // categories this command exists to act on.
        var category = await _repository.GetByIdIncludingInactiveAsync(request.CategoryId, cancellationToken);

        if (category is null)
            return Result.Failure(new Error("Category.NotFound", $"Category with ID '{request.CategoryId}' was not found."));

        if (category.IsActive)
            return Result.Failure(new Error("Category.NotDeleted", $"Category with ID '{request.CategoryId}' is not deleted."));

        // Restoring a child of a still-deleted parent would produce a category that is active but
        // unreachable from the root tree — present in the data, invisible in every read. Refused
        // rather than silently cascading a reactivation of the parent, which is a bigger decision
        // than this request expressed.
        if (category.ParentCategoryId is { } parentId)
        {
            var parent = await _repository.GetByIdIncludingInactiveAsync(parentId, cancellationToken);
            if (parent is null || !parent.IsActive)
            {
                return Result.Failure(new Error("Category.ParentNotActive",
                    $"Parent category '{parentId}' is deleted. Restore it first, or move this category before restoring it."));
            }
        }

        // Both unique slug indexes are filtered on "IsActive", so this category's slug was free
        // while it was deleted and another category may have taken it. Reactivating re-enters the
        // filtered index; without this check that is a raw 23505 rather than an answer an admin can
        // act on. SlugExistsAsync runs under the same filter as the index's own predicate.
        //
        // Every check runs before Restore() mutates: TransactionBehavior commits on a failure
        // Result, so a check after the mutation would persist it anyway.
        if (await _repository.SlugExistsAsync(category.ParentCategoryId, category.Slug, cancellationToken))
        {
            return Result.Failure(new Error("Category.SlugConflict",
                $"Slug '{category.Slug}' is already used by another category at this level. Change that category's slug before restoring this one."));
        }

        category.Restore();

        await _repository.UpdateAsync(category, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _cacheInvalidationContext.AddKeys(CategoryCacheKeys.RelativesOf(category));

        return Result.Success();
    }
}
