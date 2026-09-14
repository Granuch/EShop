namespace EShop.Notification.Domain.Models;

/// <summary>
/// Notification audit S7 (L19): no status. Payment always publishes this event as <c>PROCESSING</c>, a raw enum name
/// the email's own wording ("started, nothing charged yet") already says better.
/// </summary>
public sealed record PaymentCreatedEmailModel
{
    public required Guid OrderId { get; init; }
    public required string CustomerName { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required DateTime CreatedAt { get; init; }
}
