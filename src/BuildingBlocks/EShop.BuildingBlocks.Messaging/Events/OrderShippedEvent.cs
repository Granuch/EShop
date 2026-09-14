namespace EShop.BuildingBlocks.Messaging.Events;

/// <summary>
/// Event published when an order is shipped
/// </summary>
public record OrderShippedEvent : IntegrationEvent
{
    public Guid OrderId { get; init; }
    public string UserId { get; init; } = string.Empty;

    /// <summary>
    /// <b>Always empty from Ordering</b>, which holds no email address. Notification treats empty as
    /// "resolve the recipient from <see cref="UserId"/>", so this is an optional override, not a field a
    /// consumer may rely on.
    /// </summary>
    public string UserEmail { get; init; } = string.Empty;

    /// <summary><b>Always null today</b>: no carrier integration exists to supply one.</summary>
    public string? TrackingNumber { get; init; }

    /// <summary>When the order was shipped (the domain event's time, not the publish time).</summary>
    public DateTime ShippedAt { get; init; }
}
