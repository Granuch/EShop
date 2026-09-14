namespace EShop.Notification.Domain.Interfaces;

using EShop.Notification.Domain.ValueObjects;
using EShop.Notification.Domain.Models;

/// <summary>
/// Sends the notification emails. Each method returns the sent message's Message-ID, which the consumer records as the
/// notification's provider message id (Notification audit S7, L21): the one identifier a mail server's logs share with
/// ours.
/// </summary>
public interface IEmailService
{
    Task<string> SendOrderConfirmationAsync(RecipientAddress recipient, OrderConfirmationEmailModel model, CancellationToken ct = default);
    Task<string> SendOrderShippedAsync(RecipientAddress recipient, OrderShippedEmailModel model, CancellationToken ct = default);
    Task<string> SendPaymentCreatedAsync(RecipientAddress recipient, PaymentCreatedEmailModel model, CancellationToken ct = default);
    Task<string> SendPaymentCompletedAsync(RecipientAddress recipient, PaymentCompletedEmailModel model, CancellationToken ct = default);
    Task<string> SendPaymentFailedAsync(RecipientAddress recipient, PaymentFailedEmailModel model, CancellationToken ct = default);
    Task<string> SendPaymentRefundedAsync(RecipientAddress recipient, PaymentRefundedEmailModel model, CancellationToken ct = default);
    Task<string> SendPasswordResetAsync(RecipientAddress recipient, PasswordResetEmailModel model, CancellationToken ct = default);
}
