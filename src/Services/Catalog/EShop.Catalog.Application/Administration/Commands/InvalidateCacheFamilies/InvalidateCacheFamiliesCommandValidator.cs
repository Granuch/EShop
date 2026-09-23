using FluentValidation;

namespace EShop.Catalog.Application.Administration.Commands.InvalidateCacheFamilies;

public sealed class InvalidateCacheFamiliesCommandValidator : AbstractValidator<InvalidateCacheFamiliesCommand>
{
    public InvalidateCacheFamiliesCommandValidator()
    {
        // Ordinal, not case-insensitive: the family is part of the version entry's key (cachever:products:list), so
        // "Products:List" would bump an entry no query reads — answering 200 and invalidating nothing.
        RuleFor(x => x.Family)
            .Must(f => CatalogCacheFamilies.All.Contains(f!, StringComparer.Ordinal))
            .When(x => x.Family is not null)
            .WithMessage($"'family' must be one of: {string.Join(", ", CatalogCacheFamilies.All)} — or omitted, for all of them.");
    }
}
