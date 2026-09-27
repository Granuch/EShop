using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Categories.Commands.MoveCategory;

public class MoveCategoryCommandHandler : IRequestHandler<MoveCategoryCommand, Result>
{
    private readonly ICategoryRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public MoveCategoryCommandHandler(ICategoryRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(MoveCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await _repository.GetById(request.CategoryId, cancellationToken);
        if (category is null)
            return Result.Failure(new Error("Category.NotFound", $"Category with ID '{request.CategoryId}' was not found."));

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

        // F-39 (frontend-contracts R5). Slugs are unique per level, so a move into a level where a
        // live sibling already holds this slug is refused here, with a detail naming the fixes that
        // exist. Without this check the move reached the unique index and answered with the race
        // wording ("created concurrently … retry"), which was false and could never succeed on
        // retry. A move within the same level changes nothing and needs no check — the category
        // would otherwise collide with itself. The index stays the backstop for a genuine race.
        // Checked after the cycle inputs are read but, like them, before MoveTo mutates anything.
        if (request.NewParentCategoryId != category.ParentCategoryId
            && await _repository.SlugExistsAsync(request.NewParentCategoryId, category.Slug, cancellationToken))
        {
            return Result.Failure(new Error("Category.SlugConflict",
                $"Another category at the target level already uses the slug '{category.Slug}'. Change this category's slug or the other one's (PUT /api/v1/categories/{{id}} with a new slug), then move it."));
        }

        // Every check above runs before MoveTo mutates anything. TransactionBehavior commits on any
        // non-exception return, so a failure Result after a mutation would persist the move.
        category.MoveTo(request.NewParentCategoryId, ancestorIds);

        await _repository.UpdateAsync(category, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
