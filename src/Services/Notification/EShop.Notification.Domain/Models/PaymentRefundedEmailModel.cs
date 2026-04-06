namespace EShop.Notification.Domain.Models;

public sealed record PaymentRefundedEmailModel
{
    public required Guid OrderId { get; init; }
    public required string CustomerName { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required DateTime RefundedAt { get; init; }
    public required string SupportEmail { get; init; }
}
