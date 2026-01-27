namespace EShop.Notification.Domain.Interfaces;

/// <summary>
/// Interface for email sending
/// </summary>
public interface IEmailService
{
    // TODO: Implement email sending methods
    Task SendOrderConfirmationAsync(OrderConfirmationEmail email, CancellationToken cancellationToken = default);
    Task SendOrderShippedAsync(OrderShippedEmail email, CancellationToken cancellationToken = default);
    Task SendPaymentFailedAsync(PaymentFailedEmail email, CancellationToken cancellationToken = default);
    Task SendWelcomeEmailAsync(WelcomeEmail email, CancellationToken cancellationToken = default);

    // TODO: Add generic SendEmailAsync with template support
}

// TODO: Define email models
public record OrderConfirmationEmail
{
    public string To { get; init; } = string.Empty;
    public Guid OrderId { get; init; }
    public decimal TotalAmount { get; init; }
    public List<OrderItemEmail> Items { get; init; } = new();
}

public record OrderItemEmail
{
    public string ProductName { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal SubTotal { get; init; }
}

public record OrderShippedEmail
{
    public string To { get; init; } = string.Empty;
    public Guid OrderId { get; init; }
    public string? TrackingNumber { get; init; }
}

public record PaymentFailedEmail
{
    public string To { get; init; } = string.Empty;
    public Guid OrderId { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public record WelcomeEmail
{
    public string To { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
}
