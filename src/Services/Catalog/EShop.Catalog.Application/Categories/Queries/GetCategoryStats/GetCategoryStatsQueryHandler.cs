using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Application.Abstractions;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Categories.Queries.GetCategoryStats;

public class GetCategoryStatsQueryHandler : IRequestHandler<GetCategoryStatsQuery, Result<CategoryStatsDto>>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly IProductQueryService _productQueryService;

    public GetCategoryStatsQueryHandler(
        ICategoryRepository categoryRepository,
        IProductQueryService productQueryService)
    {
        _categoryRepository = categoryRepository;
        _productQueryService = productQueryService;
    }

    public async Task<Result<CategoryStatsDto>> Handle(
        GetCategoryStatsQuery request,
        CancellationToken cancellationToken)
    {
        // GetById runs under the c.IsActive filter, so a deleted category answers 404 here — right,
        // because its stats are not something the admin panel offers a way to reach.
        var category = await _categoryRepository.GetById(request.CategoryId, cancellationToken);

        if (category is null)
            return Result<CategoryStatsDto>.Failure(new Error("Category.NotFound", $"Category with ID '{request.CategoryId}' was not found."));

        var stats = await _productQueryService.GetCategoryProductStatsAsync(request.CategoryId, cancellationToken);

        return Result<CategoryStatsDto>.Success(new CategoryStatsDto(
            category.Id,
            category.Name,
            stats.ProductCount,
            stats.PublishedProductCount,
            // The DTO reports int: a category whose stock exceeds int.MaxValue is not a real state,
            // and the long above exists to make the SUM safe, not to widen the contract.
            (int)Math.Min(stats.TotalStock, int.MaxValue),
            stats.OutOfStockCount,
            // ChildCategories comes from GetById's Include, which respects the IsActive filter, so
            // this counts live children only — matching what the tree read shows.
            category.ChildCategories.Count));
    }
}
