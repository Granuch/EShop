using EShop.BuildingBlocks.Application;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Categories.Queries.GetCategories;

public class GetCategoriesQueryHandler : IRequestHandler<GetCategoriesQuery, Result<List<CategoryDto>>>
{
    private readonly ICategoryRepository _categoryRepository;

    /// <summary>
    /// F-37 (frontend-contracts R5): the tree is assembled by <see cref="CategoryTree"/> from one flat
    /// read, at any depth and with every root. It used to be mapped through <c>IMapper</c> from an
    /// <c>Include</c> chain that stopped at grandchildren and cut the roots at 100.
    /// </summary>
    public GetCategoriesQueryHandler(ICategoryRepository categoryRepository)
    {
        _categoryRepository = categoryRepository;
    }

    public async Task<Result<List<CategoryDto>>> Handle(GetCategoriesQuery request, CancellationToken cancellationToken)
    {
        var categories = await _categoryRepository.GetAllAsync(
            request.EffectiveIncludeInactive, cancellationToken);

        return Result<List<CategoryDto>>.Success(CategoryTree.Roots(categories));
    }
}
