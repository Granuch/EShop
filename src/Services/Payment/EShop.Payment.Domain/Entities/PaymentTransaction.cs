namespace EShop.Payment.Domain.Entities;

/// <summary>
/// Payment transaction entity
/// </summary>
public class PaymentTransaction
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public string PaymentIntentId { get; set; } = string.Empty;
    public PaymentStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }

    // TODO: Add payment method (CreditCard, PayPal, etc.)
    // TODO: Add retry count for failed payments
}

public enum PaymentStatus
{
    Pending,
    Processing,
    Success,
    Failed,
    Refunded
}
