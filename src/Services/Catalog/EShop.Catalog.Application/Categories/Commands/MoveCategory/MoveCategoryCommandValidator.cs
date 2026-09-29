using FluentValidation;

namespace EShop.Catalog.Application.Categories.Commands.MoveCategory;

/// <summary>
/// Validator for MoveCategoryCommand.
/// </summary>
/// <remarks>
/// The self-parent check is here as well as in <c>Category.MoveTo</c>, deliberately: this one needs
/// only the request and gives a field-level 400, while the domain's covers any caller that does not
/// come through this command. The descendant-cycle check cannot be here — it needs the persisted
/// ancestor chain.
/// </remarks>
public class MoveCategoryCommandValidator : AbstractValidator<MoveCategoryCommand>
{
    public MoveCategoryCommandValidator()
    {
        RuleFor(x => x.CategoryId)
            .NotEmpty().WithMessage("Category ID is required");

        RuleFor(x => x)
            .Must(x => x.NewParentCategoryId != x.CategoryId)
                .WithMessage("A category cannot be its own parent");

        // Guid.Empty is not a valid parent id; null is, and means "make this a root".
        RuleFor(x => x.NewParentCategoryId)
            .NotEmpty().WithMessage("New parent category ID must not be an empty GUID (send null to make this a root category)")
            .When(x => x.NewParentCategoryId.HasValue);
    }
}
