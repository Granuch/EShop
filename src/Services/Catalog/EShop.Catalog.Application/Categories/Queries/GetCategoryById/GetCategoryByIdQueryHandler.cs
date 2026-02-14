using EShop.BuildingBlocks.Application;
using EShop.Catalog.Domain.Interfaces;
using MapsterMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Catalog.Application.Categories.Queries.GetCategoryById;

public sealed class GetCategoryByIdQueryHandler : IRequestHandler<GetCategoryByIdQuery, Result<CategoryDto>>
{
    private readonly ILogger<GetCategoryByIdQueryHandler> _logger;
    private readonly ICategoryRepository _categoryRepository;
    private readonly IMapper _mapper;

    public GetCategoryByIdQueryHandler(ILogger<GetCategoryByIdQueryHandler> logger, ICategoryRepository categoryRepository, IMapper mapper)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _categoryRepository = categoryRepository ?? throw new ArgumentNullException(nameof(categoryRepository));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
    }

    public async Task<Result<CategoryDto>> Handle(GetCategoryByIdQuery request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Fetching category with id {requestId}", request.Id);

        var category = await _categoryRepository.GetById(request.Id, cancellationToken);

        if (category is null)
        {
            _logger.LogWarning("Category with id {requestId} does not exist", request.Id);
            return Result<CategoryDto>.Failure(new Error("404", "Not found"));
        }
        
        var categotyDto = _mapper.Map<CategoryDto>(category);
        _logger.LogInformation("Succesfully fetched category with id {requestId}", request.Id);
        return Result<CategoryDto>.Success(categotyDto);
    }
}