using FluentValidation;

namespace EShop.Catalog.Application.Categories.Commands.ReorderCategories;

/// <summary>
/// Validator for ReorderCategoriesCommand. It checks only what the request can be judged on by
/// itself; "exactly this level's siblings, each once" needs the persisted tree and lives in the
/// handler.
/// </summary>
public class ReorderCategoriesCommandValidator : AbstractValidator<ReorderCategoriesCommand>
{
    public ReorderCategoriesCommandValidator()
    {
        // Cascade(Stop) rather than a trailing .When(...): a trailing When guards every rule above
        // it in the chain, so a rule added later becomes a silent no-op.
        RuleFor(x => x.CategoryIds)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("The ordered list of category IDs is required")
            .Must(ids => ids!.Count > 0).WithMessage("At least one category ID is required");

        RuleForEach(x => x.CategoryIds)
            .NotEmpty().WithMessage("Category IDs must not be empty");
    }
}
