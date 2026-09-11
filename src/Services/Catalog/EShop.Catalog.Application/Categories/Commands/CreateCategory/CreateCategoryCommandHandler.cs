using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Categories.Commands.CreateCategory;

public class CreateCategoryCommandHandler : IRequestHandler<CreateCategoryCommand, Result<Guid>>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateCategoryCommandHandler(ICategoryRepository categoryRepository, IUnitOfWork unitOfWork)
    {
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateCategoryCommand request, CancellationToken cancellationToken)
    {
        Category? parent = null;

        if (request.ParentCategoryId is { } parentId)
        {
            // The global query filter hides soft-deleted categories, so a deleted parent is
            // "not found" here — which is the right answer for a client.
            parent = await _categoryRepository.GetById(parentId, cancellationToken);
            if (parent is null)
            {
                return Result<Guid>.Failure(new Error(
                    "Category.ParentNotFound",
                    $"Parent category with ID '{parentId}' was not found."));
            }
        }

        // M9. Checked BEFORE Category.Create, not after: Create attaches the new category to its
        // tracked parent, and TransactionBehavior commits even on a failure Result — so a conflict
        // detected afterwards would still be inserted, via the parent. This read-then-write check
        // answers the ordinary duplicate with a 400; a concurrent one reaches the unique index and
        // CatalogProblemDetailsExtensions.AddCategorySlugConflict answers it with a 409.
        var slug = Category.ResolveRequestedSlug(request.Slug, request.Name);
        if (slug is not null && await _categoryRepository.SlugExistsAsync(parent?.Id, slug, cancellationToken))
        {
            return Result<Guid>.Failure(new Error(
                "Category.SlugConflict",
                $"A category with the slug '{slug}' already exists at this level."));
        }

        var category = Category.Create(
            request.Name,
            request.Slug,
            parent,
            request.Description,
            request.DisplayOrder ?? 0);

        await _categoryRepository.AddAsync(category, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(category.Id);
    }
}
