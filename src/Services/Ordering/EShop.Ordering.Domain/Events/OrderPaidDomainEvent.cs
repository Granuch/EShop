using EShop.BuildingBlocks.Domain;

namespace EShop.Ordering.Domain.Events;

/// <summary>
/// Event raised when order payment is confirmed
/// </summary>
public record OrderPaidDomainEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredOn { get; } = DateTime.UtcNow;

    public Guid OrderId { get; init; }
    public string UserId { get; init; } = string.Empty;
    public string PaymentIntentId { get; init; } = string.Empty;
}
