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

        // F-39. Checked before any mutation: TransactionBehavior commits on a failure Result, so a
        // rename applied first would be persisted by the conflict that followed it. Re-sending the
        // current slug is not a conflict — the category would otherwise collide with itself. A
        // concurrent taker slips past this read and reaches the IsActive-filtered unique index,
        // which AddCategorySlugConflict() answers with the same 409 code.
        var slugChanged = request.Slug is not null && !string.Equals(request.Slug, category.Slug, StringComparison.Ordinal);
        if (slugChanged && await _repository.SlugExistsAsync(category.ParentCategoryId, request.Slug!, cancellationToken))
        {
            return Result.Failure(new Error("Category.SlugConflict",
                $"A category with the slug '{request.Slug}' already exists at this level. Choose a different slug."));
        }

        category.UpdateCategory(request.Name, request.Description, request.DisplayOrder);

        if (slugChanged)
            category.ChangeSlug(request.Slug!);

        await _repository.UpdateAsync(category, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
