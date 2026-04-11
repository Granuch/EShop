namespace EShop.Payment.Domain.Entities;

/// <summary>
/// Payment transaction entity
/// </summary>
public class PaymentTransaction
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? StripeCustomerId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string PaymentMethod { get; set; } = "Mock";
    public string PaymentIntentId { get; set; } = string.Empty;
    public string? StripeStatus { get; set; }
    public PaymentStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public uint Version { get; set; }
}

public enum PaymentStatus
{
    Pending,
    Processing,
    Success,
    Failed,
    Refunded
}
