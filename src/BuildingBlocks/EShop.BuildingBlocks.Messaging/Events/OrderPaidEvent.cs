namespace EShop.BuildingBlocks.Messaging.Events;

/// <summary>
/// Event published when an order is paid
/// </summary>
public record OrderPaidEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public string PaymentIntentId { get; init; } = string.Empty;
}
