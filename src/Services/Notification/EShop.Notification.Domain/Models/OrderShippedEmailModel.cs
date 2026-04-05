namespace EShop.Notification.Domain.Models;

public sealed record OrderShippedEmailModel
{
    public required Guid OrderId { get; init; }
    public required string CustomerName { get; init; }
    public string? TrackingNumber { get; init; }
    public required string EstimatedDelivery { get; init; }
}
