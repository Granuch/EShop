using EShop.BuildingBlocks.Application;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Categories.Queries.GetCategoryById;

/// <summary>
/// One live category with its whole live subtree (F-37, frontend-contracts R5). It used to include
/// one level of children, whose own <c>childCategories</c> were always <c>[]</c> — so a client could
/// not tell a leaf from a node that simply had not been loaded.
/// </summary>
/// <remarks>
/// Reads every live category and cuts the subtree out in memory: one query at any depth, and
/// categories are few. A deleted category is absent from that read, so it answers
/// <c>Category.NotFound</c> as it always has.
/// </remarks>
public sealed class GetCategoryByIdQueryHandler : IRequestHandler<GetCategoryByIdQuery, Result<CategoryDto>>
{
    private readonly ICategoryRepository _categoryRepository;

    public GetCategoryByIdQueryHandler(ICategoryRepository categoryRepository)
    {
        _categoryRepository = categoryRepository;
    }

    public async Task<Result<CategoryDto>> Handle(GetCategoryByIdQuery request, CancellationToken cancellationToken)
    {
        var categories = await _categoryRepository.GetAllAsync(includeInactive: false, cancellationToken);

        return CategoryTree.Subtree(categories, request.Id) is { } category
            ? Result<CategoryDto>.Success(category)
            : Result<CategoryDto>.Failure(new Error("Category.NotFound", $"Category with ID '{request.Id}' was not found."));
    }
}
