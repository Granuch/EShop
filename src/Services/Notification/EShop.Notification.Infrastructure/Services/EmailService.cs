using System.Globalization;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Domain.Models;
using EShop.Notification.Domain.ValueObjects;
using EShop.Notification.Infrastructure.Configuration;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Utils;

namespace EShop.Notification.Infrastructure.Services;

public sealed class EmailService : IEmailService
{
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm";
    private const string DateFormat = "yyyy-MM-dd";

    private readonly SmtpSettings _smtpSettings;
    private readonly ITemplateRenderer _templateRenderer;
    private readonly ILogger<EmailService> _logger;

    public EmailService(
        IOptions<SmtpSettings> smtpSettings,
        ITemplateRenderer templateRenderer,
        ILogger<EmailService> logger)
    {
        _smtpSettings = smtpSettings.Value;
        _templateRenderer = templateRenderer;
        _logger = logger;
    }

    public async Task<string> SendOrderConfirmationAsync(
        RecipientAddress recipient,
        OrderConfirmationEmailModel model,
        CancellationToken ct = default)
    {
        var htmlBody = await _templateRenderer.RenderAsync(
            NotificationTemplates.OrderCreated,
            new Dictionary<string, string>
            {
                ["OrderId"] = model.OrderId.ToString(),
                ["CustomerName"] = model.CustomerName,
                ["OrderDate"] = model.OrderDate.ToString(DateTimeFormat, CultureInfo.InvariantCulture),
                ["TotalAmount"] = Money(model.TotalAmount),
                // Notification audit S7 (D11): the template used to print a hard-coded dollar sign.
                ["Currency"] = model.Currency,
                ["ItemCount"] = model.ItemCount.ToString(CultureInfo.InvariantCulture)
            },
            ct);

        return await SendAsync(recipient, $"Order confirmation #{model.OrderId}", htmlBody, ct);
    }

    public async Task<string> SendPasswordResetAsync(
        RecipientAddress recipient,
        PasswordResetEmailModel model,
        CancellationToken ct = default)
    {
        var htmlBody = await _templateRenderer.RenderAsync(
            NotificationTemplates.PasswordReset,
            new Dictionary<string, string>
            {
                ["CustomerName"] = model.CustomerName,
                ["ResetLink"] = model.ResetLink
            },
            ct);

        return await SendAsync(recipient, "Reset your EShop password", htmlBody, ct);
    }

    public async Task<string> SendPaymentCreatedAsync(
        RecipientAddress recipient,
        PaymentCreatedEmailModel model,
        CancellationToken ct = default)
    {
        var htmlBody = await _templateRenderer.RenderAsync(
            NotificationTemplates.PaymentCreated,
            new Dictionary<string, string>
            {
                ["OrderId"] = model.OrderId.ToString(),
                ["CustomerName"] = model.CustomerName,
                ["Amount"] = Money(model.Amount),
                ["Currency"] = model.Currency,
                ["CreatedAt"] = model.CreatedAt.ToString(DateTimeFormat, CultureInfo.InvariantCulture)
            },
            ct);

        // Payment audit Stage 8 (M5): nothing has been charged yet when this is sent, so it must not say "received".
        return await SendAsync(recipient, $"Payment started for order #{model.OrderId}", htmlBody, ct);
    }

    public async Task<string> SendPaymentCompletedAsync(
        RecipientAddress recipient,
        PaymentCompletedEmailModel model,
        CancellationToken ct = default)
    {
        var htmlBody = await _templateRenderer.RenderAsync(
            NotificationTemplates.PaymentCompleted,
            new Dictionary<string, string>
            {
                ["OrderId"] = model.OrderId.ToString(),
                ["CustomerName"] = model.CustomerName,
                ["Amount"] = Money(model.Amount),
                ["Currency"] = model.Currency,
                ["CompletedAt"] = model.CompletedAt.ToString(DateTimeFormat, CultureInfo.InvariantCulture)
            },
            ct);

        return await SendAsync(recipient, $"Payment successful for order #{model.OrderId}", htmlBody, ct);
    }

    public async Task<string> SendPaymentRefundedAsync(
        RecipientAddress recipient,
        PaymentRefundedEmailModel model,
        CancellationToken ct = default)
    {
        var htmlBody = await _templateRenderer.RenderAsync(
            NotificationTemplates.PaymentRefunded,
            new Dictionary<string, string>
            {
                ["OrderId"] = model.OrderId.ToString(),
                ["CustomerName"] = model.CustomerName,
                ["Amount"] = Money(model.Amount),
                ["Currency"] = model.Currency,
                ["RefundedAt"] = model.RefundedAt.ToString(DateTimeFormat, CultureInfo.InvariantCulture),
                ["SupportEmail"] = model.SupportEmail
            },
            ct);

        return await SendAsync(recipient, $"Refund processed for order #{model.OrderId}", htmlBody, ct);
    }

    public async Task<string> SendOrderShippedAsync(
        RecipientAddress recipient,
        OrderShippedEmailModel model,
        CancellationToken ct = default)
    {
        var htmlBody = await _templateRenderer.RenderAsync(
            NotificationTemplates.OrderShipped,
            new Dictionary<string, string>
            {
                ["OrderId"] = model.OrderId.ToString(),
                ["CustomerName"] = model.CustomerName,
                ["TrackingNumber"] = model.TrackingNumber ?? "Not available yet",
                ["ShippedAt"] = model.ShippedAt.ToString(DateFormat, CultureInfo.InvariantCulture)
            },
            ct);

        return await SendAsync(recipient, $"Your order #{model.OrderId} has shipped", htmlBody, ct);
    }

    public async Task<string> SendPaymentFailedAsync(
        RecipientAddress recipient,
        PaymentFailedEmailModel model,
        CancellationToken ct = default)
    {
        var htmlBody = await _templateRenderer.RenderAsync(
            NotificationTemplates.PaymentFailed,
            new Dictionary<string, string>
            {
                ["OrderId"] = model.OrderId.ToString(),
                ["CustomerName"] = model.CustomerName,
                ["FailureReason"] = model.FailureReason,
                ["SupportEmail"] = model.SupportEmail
            },
            ct);

        // Notification audit S7 (M12, D10): Ordering cancels the order when its payment fails.
        return await SendAsync(recipient, $"Your order #{model.OrderId} was cancelled", htmlBody, ct);
    }

    private static string Money(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);

    private MimeMessage BuildMessage(RecipientAddress recipient, string subject, string htmlBody)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_smtpSettings.FromName, _smtpSettings.FromEmail));
        message.To.Add(new MailboxAddress(recipient.DisplayName ?? recipient.Email, recipient.Email));
        message.Subject = subject;
        message.MessageId = MimeUtils.GenerateMessageId();

        // Notification audit S7 (L21): a plain-text part beside the HTML.
        message.Body = new BodyBuilder
        {
            HtmlBody = htmlBody,
            TextBody = PlainTextBody.FromHtml(htmlBody)
        }.ToMessageBody();

        return message;
    }

    /// <returns>The sent message's Message-ID (S7, L21).</returns>
    private async Task<string> SendAsync(RecipientAddress recipient, string subject, string htmlBody, CancellationToken ct)
    {
        var message = BuildMessage(recipient, subject, htmlBody);

        using var smtpClient = new SmtpClient();
        smtpClient.CheckCertificateRevocation = _smtpSettings.CheckCertificateRevocation;

        await smtpClient.ConnectAsync(
            _smtpSettings.Host,
            _smtpSettings.Port,
            _smtpSettings.EffectiveSecurity.ToSocketOptions(),
            ct);

        if (!string.IsNullOrWhiteSpace(_smtpSettings.Username))
        {
            await smtpClient.AuthenticateAsync(_smtpSettings.Username, _smtpSettings.Password, ct);
        }

        var response = await smtpClient.SendAsync(message, ct);

        // Notification audit S2 (H3, D2). The server has accepted the message, so nothing after this may fail the send, or
        // the consumer records it Failed and the redelivery sends it again. MailKit already ignores a failed QUIT (S5 found
        // this: EmailServiceSmtpTests stays green without this catch); the catch covers whatever else it may not.
        try
        {
            await smtpClient.DisconnectAsync(true, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The SMTP connection could not be closed cleanly after a successful send.");
        }

        // Notification audit S5 (M7). This logged the recipient's name and address for every email, to the console, the
        // log files and Seq. The consumer's own log lines carry the EventId, which finds the recipient in NotificationLogs.
        _logger.LogInformation(
            "Email sent. MessageId={MessageId}. Subject={Subject}. ProviderResponse={ProviderResponse}",
            message.MessageId,
            message.Subject,
            response);

        return message.MessageId;
    }
}
