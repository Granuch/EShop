using FluentValidation;

namespace EShop.Catalog.Application.Products.Commands.ImportProducts;

/// <summary>
/// The envelope only: present, non-empty, within the cap. Each row's own fields are checked by the handler and reported
/// per row — a validator rule here would refuse the whole file for one bad row.
/// </summary>
public class ImportProductsCommandValidator : AbstractValidator<ImportProductsCommand>
{
    public ImportProductsCommandValidator()
    {
        RuleFor(x => x.Products)
            .NotEmpty().WithMessage("At least one product row is required")
            .Must(rows => rows is null || rows.Count <= ImportProductsCommand.MaxRows)
                .WithMessage($"An import may contain at most {ImportProductsCommand.MaxRows} rows")
            // A JSON null inside the array binds as a null row; refused here rather than dereferenced in the handler.
            .Must(rows => rows is null || rows.All(r => r is not null))
                .WithMessage("A product row must not be null");
    }
}
