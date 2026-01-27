namespace EShop.BuildingBlocks.Messaging.Events;

/// <summary>
/// Event published when payment succeeds
/// </summary>
public record PaymentSuccessEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public string PaymentIntentId { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public DateTime ProcessedAt { get; init; }
}
