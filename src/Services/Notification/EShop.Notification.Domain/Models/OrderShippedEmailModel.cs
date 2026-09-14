namespace EShop.Notification.Domain.Models;

/// <summary>
/// Notification audit S7 (L18). It used to carry an "estimated delivery" of the ship date plus five days, which nothing
/// supplied; the email now states only what the event knows.
/// </summary>
public sealed record OrderShippedEmailModel
{
    public required Guid OrderId { get; init; }
    public required string CustomerName { get; init; }

    /// <summary>Always null today: no carrier integration supplies one.</summary>
    public string? TrackingNumber { get; init; }

    public required DateTime ShippedAt { get; init; }
}
