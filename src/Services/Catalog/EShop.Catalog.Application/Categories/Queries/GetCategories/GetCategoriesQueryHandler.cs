using EShop.BuildingBlocks.Application;
using EShop.Catalog.Domain.Interfaces;
using Mapster;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Catalog.Application.Categories.Queries.GetCategories;

public class GetCategoriesQueryHandler : IRequestHandler<GetCategoriesQuery, Result<List<CategoryDto>>>
{
    private readonly ILogger<GetCategoriesQueryHandler> _logger;
    private readonly ICategoryRepository _categoryRepository;

    public GetCategoriesQueryHandler(ILogger<GetCategoriesQueryHandler> logger, ICategoryRepository categoryRepository)
    {
        _logger = logger;
        _categoryRepository = categoryRepository;
    }

    public async Task<Result<List<CategoryDto>>> Handle(GetCategoriesQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching all categories");
        var categories = await _categoryRepository.GetRootCategories(cancellationToken);

        var dto = categories.Adapt<List<CategoryDto>>();
        return Result<List<CategoryDto>>.Success(dto);
    }
}