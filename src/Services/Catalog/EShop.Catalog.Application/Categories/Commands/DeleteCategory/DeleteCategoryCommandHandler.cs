using EShop.BuildingBlocks.Application;
using EShop.Catalog.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace EShop.Catalog.Application.Categories.Commands.DeleteCategory;

public class DeleteCategoryCommandHandler : IRequestHandler<DeleteCategoryCommand, Result>
{
    private readonly ILogger<DeleteCategoryCommandHandler> _logger;
    private readonly ICategoryRepository _categoryRepository;

    public DeleteCategoryCommandHandler(ILogger<DeleteCategoryCommandHandler> logger, ICategoryRepository categoryRepository)
    {
        _logger = logger;
        _categoryRepository = categoryRepository;
    }
    
    public async Task<Result> Handle(DeleteCategoryCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting category with id {id}", request.Id);
        var category = await _categoryRepository.GetById(request.Id, cancellationToken);
        if (category is null)
            return Result.Failure(new Error("Not found", $"Product with id {request.Id} not found"));
        
        await _categoryRepository.DeleteAsync(category, cancellationToken);
        _logger.LogInformation("Deleted category with id {id}", request.Id);
        return Result.Success();
    }
}