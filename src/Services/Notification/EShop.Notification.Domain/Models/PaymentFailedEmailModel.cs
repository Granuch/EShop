namespace EShop.Notification.Domain.Models;

public sealed record PaymentFailedEmailModel
{
    public required Guid OrderId { get; init; }
    public required string CustomerName { get; init; }
    public required string FailureReason { get; init; }
    public required string SupportEmail { get; init; }
}
