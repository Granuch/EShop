using EShop.Catalog.Domain.Entities;
using FluentValidation;

namespace EShop.Catalog.Application.Categories.Commands.CreateCategory;

public class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Category name is required")
            .MaximumLength(200).WithMessage("Category name must not exceed 200 characters");

        // F-39. Blank means "derive it from the name", as the domain reads it, so the rules apply
        // only to a slug with content — and to it exactly as sent: surrounding spaces or capitals are
        // refused rather than silently normalised into a slug the client did not ask for.
        RuleFor(x => x.Slug)
            .Cascade(CascadeMode.Stop)
            .MaximumLength(Category.SlugMaxLength).WithMessage("Slug must not exceed 200 characters")
            .Matches(Category.SlugPattern).WithMessage(Category.InvalidSlugMessage)
            .When(x => !string.IsNullOrWhiteSpace(x.Slug));

        // Same cap as UpdateCategoryCommandValidator, so a description that can be created can
        // also be kept through an update.
        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Description must not exceed 1000 characters");

        RuleFor(x => x.DisplayOrder)
            .GreaterThanOrEqualTo(0).When(x => x.DisplayOrder.HasValue)
            .WithMessage("Display order cannot be negative");
    }
}
