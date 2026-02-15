using EShop.BuildingBlocks.Application;
using EShop.Catalog.Domain.Interfaces;
using Mapster;
using MediatR;

namespace EShop.Catalog.Application.Categories.Queries.GetCategories;

public class GetCategoriesQueryHandler : IRequestHandler<GetCategoriesQuery, Result<List<CategoryDto>>>
{
    private readonly ICategoryRepository _categoryRepository;

    public GetCategoriesQueryHandler(ICategoryRepository categoryRepository)
    {
        _categoryRepository = categoryRepository;
    }

    public async Task<Result<List<CategoryDto>>> Handle(GetCategoriesQuery request, CancellationToken cancellationToken)
    {
        var categories = await _categoryRepository.GetRootCategories(cancellationToken);

        var dto = categories.Adapt<List<CategoryDto>>();
        return Result<List<CategoryDto>>.Success(dto);
    }
}