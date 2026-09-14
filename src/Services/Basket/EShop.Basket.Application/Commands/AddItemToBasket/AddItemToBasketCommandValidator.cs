using EShop.Basket.Domain.Entities;
using FluentValidation;

namespace EShop.Basket.Application.Commands.AddItemToBasket;

public class AddItemToBasketCommandValidator : AbstractValidator<AddItemToBasketCommand>
{
    public AddItemToBasketCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User ID is required");

        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("Product ID is required");

        // Basket audit S9 (M2): the domain checks the line's total after the add; this refuses an impossible request early.
        RuleFor(x => x.Quantity)
            .GreaterThan(0).WithMessage("Quantity must be greater than zero")
            .LessThanOrEqualTo(ShoppingBasket.MaxQuantityPerLine)
            .WithMessage($"Quantity cannot exceed {ShoppingBasket.MaxQuantityPerLine}");
    }
}
