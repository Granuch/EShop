using EShop.Ordering.Domain.Entities;
using FluentValidation;

namespace EShop.Ordering.Application.Orders.Commands.CreateCheckedOutOrder;

/// <summary>
/// A failure here is a Result, which BasketCheckedOutConsumer turns into an error-queue message — so
/// every rule the domain or the database would enforce belongs here, where it is reported with its
/// field, rather than surfacing as an exception from deep in the handler.
/// </summary>
public class CreateCheckedOutOrderCommandValidator : AbstractValidator<CreateCheckedOutOrderCommand>
{
    public CreateCheckedOutOrderCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User ID is required");

        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("Order must have at least one item");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId)
                .NotEmpty().WithMessage("Product ID is required");

            item.RuleFor(i => i.ProductName)
                .NotEmpty().WithMessage("Product name is required")
                .MaximumLength(OrderItem.MaxProductNameLength)
                .WithMessage($"Product name must not exceed {OrderItem.MaxProductNameLength} characters");

            item.RuleFor(i => i.UnitPrice)
                .GreaterThanOrEqualTo(0).WithMessage("Price cannot be negative");

            item.RuleFor(i => i.Quantity)
                .GreaterThan(0).WithMessage("Quantity must be greater than 0");
        });

        RuleFor(x => x).Custom((command, context) => ShippingAddressRules.Check(
            context, command.Street, command.City, command.State, command.ZipCode, command.Country));
    }
}
