namespace EShop.Notification.Domain.Models;

public sealed record PaymentCompletedEmailModel
{
    public required Guid OrderId { get; init; }
    public required string CustomerName { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string PaymentIntentId { get; init; }
    public required DateTime CompletedAt { get; init; }
}
