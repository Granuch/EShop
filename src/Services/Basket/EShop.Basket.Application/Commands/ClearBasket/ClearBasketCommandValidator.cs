using FluentValidation;

namespace EShop.Basket.Application.Commands.ClearBasket;

public class ClearBasketCommandValidator : AbstractValidator<ClearBasketCommand>
{
    public ClearBasketCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User ID is required");
    }
}
