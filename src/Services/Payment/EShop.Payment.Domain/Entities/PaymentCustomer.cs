namespace EShop.Payment.Domain.Entities;

public class PaymentCustomer
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string StripeCustomerId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
