using EShop.BuildingBlocks.Application;
using EShop.Catalog.Domain.Interfaces;
using MapsterMapper;
using MediatR;

namespace EShop.Catalog.Application.Categories.Queries.GetCategories;

public class GetCategoriesQueryHandler : IRequestHandler<GetCategoriesQuery, Result<List<CategoryDto>>>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly IMapper _mapper;

    /// <summary>
    /// Maps through the injected <see cref="IMapper"/>, like <c>GetCategoryByIdQueryHandler</c>. It used
    /// the static <c>Adapt&lt;&gt;</c>, which reads <c>TypeAdapterConfig.GlobalSettings</c> directly and
    /// agreed with the injected mapper only because <c>Program.cs</c> happens to register that same
    /// global config — a move to a scoped config would have silently dropped
    /// <c>ParentCategoryName</c> and <c>PreserveReference</c> from this endpoint alone.
    /// </summary>
    public GetCategoriesQueryHandler(ICategoryRepository categoryRepository, IMapper mapper)
    {
        _categoryRepository = categoryRepository;
        _mapper = mapper;
    }

    public async Task<Result<List<CategoryDto>>> Handle(GetCategoriesQuery request, CancellationToken cancellationToken)
    {
        var categories = await _categoryRepository.GetRootCategories(cancellationToken);

        var dto = _mapper.Map<List<CategoryDto>>(categories);
        return Result<List<CategoryDto>>.Success(dto);
    }
}
