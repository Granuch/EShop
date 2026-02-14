using EShop.BuildingBlocks.Application;
using EShop.Catalog.Domain.Interfaces;
using MapsterMapper;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Catalog.Application.Categories.Commands.UpdateCategory;

public class UpdateCategoryCommandHandler : IRequestHandler<UpdateCategoryCommand, Result>
{
    private readonly ICategoryRepository _repository;
    private readonly ILogger<UpdateCategoryCommandHandler> _logger;

    public UpdateCategoryCommandHandler(ICategoryRepository repository, ILogger<UpdateCategoryCommandHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<Result> Handle(UpdateCategoryCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating Category with id: {categoryId}", request.Id);
        var category = await _repository.GetById(request.Id, cancellationToken);
        if(category is null)
            return Result.Failure(new Error("Category not found",  $"Category {request.Id} not found"));
        
        category.UpdateCategory(request.Name, request.Description);
        await  _repository.UpdateAsync(category, cancellationToken);
        return Result.Success();
    }
}