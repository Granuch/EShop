using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Categories.Commands.UpdateCategory;

public class UpdateCategoryCommandHandler : IRequestHandler<UpdateCategoryCommand, Result>
{
    private readonly ICategoryRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateCategoryCommandHandler(ICategoryRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await _repository.GetById(request.Id, cancellationToken);
        if (category is null)
            return Result.Failure(new Error("Category.NotFound", $"Category with ID '{request.Id}' was not found."));

        category.UpdateCategory(request.Name, request.Description);
        await _repository.UpdateAsync(category, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}