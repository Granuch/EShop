namespace EShop.Notification.Domain.Models;

public sealed record PaymentCreatedEmailModel
{
    public required Guid OrderId { get; init; }
    public required string CustomerName { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required string Status { get; init; }
    public required DateTime CreatedAt { get; init; }
}
