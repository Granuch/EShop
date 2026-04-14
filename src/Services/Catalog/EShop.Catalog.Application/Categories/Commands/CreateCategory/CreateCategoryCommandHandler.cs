using EShop.BuildingBlocks.Application;
using EShop.BuildingBlocks.Domain;
using EShop.Catalog.Domain.Entities;
using EShop.Catalog.Domain.Interfaces;
using MediatR;

namespace EShop.Catalog.Application.Categories.Commands.CreateCategory;

public class CreateCategoryCommandHandler : IRequestHandler<CreateCategoryCommand, Result<Guid>>
{
    private readonly ICategoryRepository _categoryRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateCategoryCommandHandler(ICategoryRepository categoryRepository, IUnitOfWork unitOfWork)
    {
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(CreateCategoryCommand request, CancellationToken cancellationToken)
    {
        Category category;

        if (request.ParentCategoryId.HasValue)
        {
            var parentCategory = await _categoryRepository.GetById(request.ParentCategoryId.Value, cancellationToken);
            if (parentCategory is null)
            {
                return Result<Guid>.Failure(new Error(
                    "Category.ParentNotFound",
                    $"Parent category with ID '{request.ParentCategoryId.Value}' was not found."));
            }

            category = Category.Create(request.Name, request.Slug, null);
            category.SetParent(parentCategory);
        }
        else
        {
            category = Category.Create(request.Name, request.Slug, null);
        }

        await _categoryRepository.AddAsync(category, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<Guid>.Success(category.Id);
    }
}