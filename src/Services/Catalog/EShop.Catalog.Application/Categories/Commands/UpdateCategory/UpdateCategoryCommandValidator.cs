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
    }
}
