namespace EShop.Notification.Domain.Models;

public sealed record OrderConfirmationEmailModel
{
    public required Guid OrderId { get; init; }
    public required string CustomerName { get; init; }
    public required DateTimeOffset OrderDate { get; init; }
    public required decimal TotalAmount { get; init; }

    /// <summary>The currency of <see cref="TotalAmount"/> (Notification audit D11).</summary>
    public required string Currency { get; init; }

    /// <summary>How many units were ordered — the sum of the lines' quantities, not the number of lines (S7, L17).</summary>
    public required int ItemCount { get; init; }
}
