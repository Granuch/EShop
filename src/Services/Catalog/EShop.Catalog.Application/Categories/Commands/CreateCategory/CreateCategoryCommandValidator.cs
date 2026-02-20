using FluentValidation;

namespace EShop.Catalog.Application.Categories.Commands.CreateCategory;

public class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Category name is required")
            .MaximumLength(200).WithMessage("Category name must not exceed 200 characters");

        RuleFor(x => x.Slug)
            .MaximumLength(200).When(x => !string.IsNullOrEmpty(x.Slug))
            .WithMessage("Slug must not exceed 200 characters");
    }
}
