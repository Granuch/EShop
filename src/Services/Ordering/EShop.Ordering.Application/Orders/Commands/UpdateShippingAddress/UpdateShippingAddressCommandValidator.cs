using FluentValidation;

namespace EShop.Ordering.Application.Orders.Commands.UpdateShippingAddress;

/// <summary>
/// Reports <c>Address.Validate</c> field by field through <see cref="ShippingAddressRules"/>, like
/// both create validators — so an address this endpoint accepts is one the value object accepts, and
/// there is no second copy of the rules to drift.
/// </summary>
public class UpdateShippingAddressCommandValidator : AbstractValidator<UpdateShippingAddressCommand>
{
    public UpdateShippingAddressCommandValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty().WithMessage("Order ID is required");

        RuleFor(x => x).Custom((command, context) => ShippingAddressRules.Check(
            context, command.Street, command.City, command.State, command.ZipCode, command.Country));
    }
}
