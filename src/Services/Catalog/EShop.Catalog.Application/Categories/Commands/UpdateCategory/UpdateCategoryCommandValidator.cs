using EShop.Catalog.Domain.Entities;
using FluentValidation;

namespace EShop.Catalog.Application.Categories.Commands.UpdateCategory;

public class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Category ID is required");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Category name is required")
            .MaximumLength(200).WithMessage("Category name must not exceed 200 characters");

        // Null (omitted) passes: MaximumLength ignores null, which is what keeps the M10 "omitted
        // leaves it" contract reachable.
        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Description must not exceed 1000 characters");

        RuleFor(x => x.DisplayOrder)
            .GreaterThanOrEqualTo(0).When(x => x.DisplayOrder.HasValue)
            .WithMessage("Display order cannot be negative");

        // F-39. Omitted (null) leaves the slug alone. Anything sent must be a valid slug — blank
        // included: a category always has one, so "" cannot mean "clear it", and silently reading it
        // as "leave it" would hide a client bug.
        RuleFor(x => x.Slug)
            .Cascade(CascadeMode.Stop)
            .MaximumLength(Category.SlugMaxLength).WithMessage("Slug must not exceed 200 characters")
            .Matches(Category.SlugPattern).WithMessage(Category.InvalidSlugMessage)
            .When(x => x.Slug is not null);
    }
}
