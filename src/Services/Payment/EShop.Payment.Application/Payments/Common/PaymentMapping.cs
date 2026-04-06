using EShop.Payment.Domain.Entities;

namespace EShop.Payment.Application.Payments.Common;

internal static class PaymentMapping
{
    public static PaymentDto ToDto(this PaymentTransaction payment)
    {
        return new PaymentDto(
            payment.Id,
            payment.OrderId,
            payment.UserId,
            payment.Amount,
            payment.Currency,
            payment.PaymentMethod,
            payment.Status.ToString().ToUpperInvariant(),
            string.IsNullOrWhiteSpace(payment.PaymentIntentId) ? null : payment.PaymentIntentId,
            payment.ErrorMessage,
            payment.CreatedAt,
            payment.ProcessedAt,
            payment.UpdatedAt);
    }
}
