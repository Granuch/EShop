using EShop.BuildingBlocks.Application;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Catalog.Application.Categories.Commands.CreateCategory;

public class CreateCategoryCommandHandler : IRequestHandler<CreateCategoryCommand, Result>
{
    private readonly ILogger<CreateCategoryCommandHandler> _logger;
    private readonly ICategoryRepository _categoryRepository;

    public CreateCategoryCommandHandler(ILogger<CreateCategoryCommandHandler> logger, ICategoryRepository categoryRepository)
    {
        _logger = logger;
        _categoryRepository = categoryRepository;
    }

    public async Task<Result> Handle(CreateCategoryCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating new category");
        
        var category = Category.Create(request.Name, request.Slug, request.ParentCategoryId);
        
        await _categoryRepository.AddAsync(category, cancellationToken);
        _logger.LogInformation("Category created");
        return Result.Success();
    }
}