using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Categories.Commands.MoveCategory;

public class MoveCategoryCommandHandler : IRequestHandler<MoveCategoryCommand, Result>
{
    private readonly ICategoryRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheInvalidationContext _cacheInvalidationContext;

    public MoveCategoryCommandHandler(
        ICategoryRepository repository,
        IUnitOfWork unitOfWork,
        ICacheInvalidationContext cacheInvalidationContext)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _cacheInvalidationContext = cacheInvalidationContext;
    }

    public async Task<Result> Handle(MoveCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await _repository.GetById(request.CategoryId, cancellationToken);
        if (category is null)
            return Result.Failure(new Error("Category.NotFound", $"Category with ID '{request.CategoryId}' was not found."));

        var oldParentId = category.ParentCategoryId;

        // Read the ancestor chain from the DATABASE, never from the ParentCategory navigation. EF
        // populates that only as far as the query Included, so walking it answers "no cycle" for
        // any chain deeper than was loaded — the Stage 8 defect that made parents immutable.
        var ancestorIds = new List<Guid>();

        if (request.NewParentCategoryId is { } newParentId)
        {
            var newParent = await _repository.GetById(newParentId, cancellationToken);
            if (newParent is null)
                return Result.Failure(new Error("Category.ParentNotFound", $"Parent category with ID '{newParentId}' was not found."));

            ancestorIds = await _repository.GetAncestorIdsAsync(newParentId, cancellationToken);
        }

        // Every check above runs before MoveTo mutates anything. TransactionBehavior commits on any
        // non-exception return, so a failure Result after a mutation would persist the move.
        category.MoveTo(request.NewParentCategoryId, ancestorIds);

        await _repository.UpdateAsync(category, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // The OLD parent's detail still lists this category as a child and cannot be named on the
        // command, which only knows where the category is going. Added here, drained by
        // CacheInvalidationBehavior after the transaction commits.
        if (oldParentId is { } previousParentId)
            _cacheInvalidationContext.AddKey(CategoryCacheKeys.Detail(previousParentId));

        // The moved category's own children each carry ParentCategoryName in their detail — that is
        // unchanged by a move, but their ancestry is not, so they are evicted for the same reason
        // RelativesOf exists.
        _cacheInvalidationContext.AddKeys(CategoryCacheKeys.RelativesOf(category));

        return Result.Success();
    }
}
