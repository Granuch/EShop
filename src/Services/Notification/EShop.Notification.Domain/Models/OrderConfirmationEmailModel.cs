namespace EShop.Notification.Domain.Models;

public sealed record OrderConfirmationEmailModel
{
    public required Guid OrderId { get; init; }
    public required string CustomerName { get; init; }
    public required DateTimeOffset OrderDate { get; init; }
    public required decimal TotalAmount { get; init; }
    public required int ItemCount { get; init; }
}
