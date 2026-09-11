using FluentValidation;

namespace EShop.Ordering.Application.Orders.Commands.CreateOrder;

/// <summary>
/// Validator for CreateOrderCommand
/// </summary>
public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User ID is required");

        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("Order must have at least one item")
            .Must(HaveDistinctProducts).WithMessage("Each product may appear only once; combine the quantities instead");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId)
                .NotEmpty().WithMessage("Product ID is required");

            item.RuleFor(i => i.Quantity)
                .GreaterThan(0).WithMessage("Quantity must be greater than 0");
        });

        RuleFor(x => x).Custom((command, context) => ShippingAddressRules.Check(
            context, command.Street, command.City, command.State, command.ZipCode, command.Country));
    }

    private static bool HaveDistinctProducts(List<CreateOrderItemDto>? items)
        => items is null || items.Select(i => i.ProductId).Distinct().Count() == items.Count;
}
