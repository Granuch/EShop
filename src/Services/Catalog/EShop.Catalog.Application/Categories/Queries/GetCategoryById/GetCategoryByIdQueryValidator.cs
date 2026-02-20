using FluentValidation;

namespace EShop.Catalog.Application.Categories.Queries.GetCategoryById;

public class GetCategoryByIdQueryValidator : AbstractValidator<GetCategoryByIdQuery>
{
    public GetCategoryByIdQueryValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Category ID is required");
    }
}
