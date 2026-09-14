namespace EShop.Notification.Domain.Models;

/// <summary>Notification audit S7 (L19): no Stripe payment intent id — an internal identifier the customer cannot use.</summary>
public sealed record PaymentCompletedEmailModel
{
    public required Guid OrderId { get; init; }
    public required string CustomerName { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required DateTime CompletedAt { get; init; }
}
