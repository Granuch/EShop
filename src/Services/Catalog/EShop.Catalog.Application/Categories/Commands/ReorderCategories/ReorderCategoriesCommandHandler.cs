using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Application.Caching;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Categories.Commands.ReorderCategories;

public class ReorderCategoriesCommandHandler : IRequestHandler<ReorderCategoriesCommand, Result>
{
    private readonly ICategoryRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheInvalidationContext _cacheInvalidationContext;

    public ReorderCategoriesCommandHandler(
        ICategoryRepository repository,
        IUnitOfWork unitOfWork,
        ICacheInvalidationContext cacheInvalidationContext)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _cacheInvalidationContext = cacheInvalidationContext;
    }

    public async Task<Result> Handle(ReorderCategoriesCommand request, CancellationToken cancellationToken)
    {
        if (request.ParentCategoryId is { } parentId)
        {
            var parent = await _repository.GetById(parentId, cancellationToken);
            if (parent is null)
                return Result.Failure(new Error("Category.NotFound", $"Category with ID '{parentId}' was not found."));
        }

        var siblings = await _repository.GetSiblingsAsync(request.ParentCategoryId, cancellationToken);
        var requestedIds = request.CategoryIds!;

        // Projected to Guid? rather than using FirstOrDefault, which returns Guid.Empty for "none
        // found" and so cannot be distinguished from a caller that supplied Guid.Empty.
        var unknownId = requestedIds
            .Where(id => siblings.All(c => c.Id != id))
            .Select(id => (Guid?)id)
            .FirstOrDefault();

        if (unknownId is not null)
        {
            return Result.Failure(new Error("Category.NotFound",
                $"Category with ID '{unknownId}' is not a child of the given parent."));
        }

        // Count and duplicate rules are a malformed request (400), not a missing resource. Both are
        // checked before any SetDisplayOrder call, because TransactionBehavior commits on a failure
        // Result and a half-applied reorder is exactly the state this endpoint exists to prevent.
        if (requestedIds.Count != siblings.Count)
        {
            return Result.Failure(new Error("Validation.IncompleteReorder",
                $"Reordering requires every sibling exactly once: this level has {siblings.Count} categor(ies) but {requestedIds.Count} id(s) were supplied."));
        }

        if (requestedIds.Distinct().Count() != requestedIds.Count)
        {
            return Result.Failure(new Error("Validation.IncompleteReorder",
                "Reordering requires every sibling exactly once: duplicate ids were supplied."));
        }

        for (var position = 0; position < requestedIds.Count; position++)
        {
            siblings.Single(c => c.Id == requestedIds[position]).SetDisplayOrder(position);
            _cacheInvalidationContext.AddKey(CategoryCacheKeys.Detail(requestedIds[position]));
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
