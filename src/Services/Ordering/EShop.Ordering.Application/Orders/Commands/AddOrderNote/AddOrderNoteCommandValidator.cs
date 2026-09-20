using EShop.Ordering.Domain.Entities;
using FluentValidation;

namespace EShop.Ordering.Application.Orders.Commands.AddOrderNote;

/// <summary>
/// The length bound is the entity's own constant, not a second copy of the number: a body the
/// validator accepts is one <see cref="OrderNote"/> accepts, and one the column holds.
/// </summary>
public class AddOrderNoteCommandValidator : AbstractValidator<AddOrderNoteCommand>
{
    public AddOrderNoteCommandValidator()
    {
        RuleFor(x => x.OrderId)
            .NotEmpty().WithMessage("Order ID is required");

        RuleFor(x => x.Body)
            .NotEmpty().WithMessage("Note body is required")
            .MaximumLength(OrderNote.MaxBodyLength)
            .WithMessage($"Note must not exceed {OrderNote.MaxBodyLength} characters");
    }
}
