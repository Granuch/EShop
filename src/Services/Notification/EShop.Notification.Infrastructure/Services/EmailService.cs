using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Domain.Models;
using EShop.Notification.Domain.ValueObjects;
using EShop.Notification.Infrastructure.Configuration;
using MailKit.Security;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace EShop.Notification.Infrastructure.Services;

public sealed class EmailService : IEmailService
{
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

    public async Task SendOrderConfirmationAsync(
        RecipientAddress recipient,
        OrderConfirmationEmailModel model,
        CancellationToken ct = default)
    {
        var subject = $"Order confirmation #{model.OrderId}";
        var htmlBody = await _templateRenderer.RenderAsync(
            "order-created",
            new Dictionary<string, string>
            {
                ["OrderId"] = model.OrderId.ToString(),
                ["CustomerName"] = model.CustomerName,
                ["OrderDate"] = model.OrderDate.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                ["TotalAmount"] = model.TotalAmount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                ["ItemCount"] = model.ItemCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
            ct);

        var message = BuildMessage(recipient, subject, htmlBody);
        await SendAsync(message, ct);
    }

    public async Task SendPasswordResetAsync(
        RecipientAddress recipient,
        PasswordResetEmailModel model,
        CancellationToken ct = default)
    {
        var subject = "Reset your EShop password";
        var htmlBody = await _templateRenderer.RenderAsync(
            "password-reset",
            new Dictionary<string, string>
            {
                ["CustomerName"] = model.CustomerName,
                ["ResetLink"] = model.ResetLink
            },
            ct);

        var message = BuildMessage(recipient, subject, htmlBody);
        await SendAsync(message, ct);
    }

    public async Task SendPaymentCreatedAsync(
        RecipientAddress recipient,
        PaymentCreatedEmailModel model,
        CancellationToken ct = default)
    {
        var subject = $"Payment received for order #{model.OrderId}";
        var htmlBody = await _templateRenderer.RenderAsync(
            "payment-created",
            new Dictionary<string, string>
            {
                ["OrderId"] = model.OrderId.ToString(),
                ["CustomerName"] = model.CustomerName,
                ["Amount"] = model.Amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                ["Currency"] = model.Currency,
                ["Status"] = model.Status,
                ["CreatedAt"] = model.CreatedAt.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)
            },
            ct);

        var message = BuildMessage(recipient, subject, htmlBody);
        await SendAsync(message, ct);
    }

    public async Task SendPaymentCompletedAsync(
        RecipientAddress recipient,
        PaymentCompletedEmailModel model,
        CancellationToken ct = default)
    {
        var subject = $"Payment successful for order #{model.OrderId}";
        var htmlBody = await _templateRenderer.RenderAsync(
            "payment-completed",
            new Dictionary<string, string>
            {
                ["OrderId"] = model.OrderId.ToString(),
                ["CustomerName"] = model.CustomerName,
                ["Amount"] = model.Amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                ["Currency"] = model.Currency,
                ["PaymentIntentId"] = model.PaymentIntentId,
                ["CompletedAt"] = model.CompletedAt.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)
            },
            ct);

        var message = BuildMessage(recipient, subject, htmlBody);
        await SendAsync(message, ct);
    }

    public async Task SendPaymentRefundedAsync(
        RecipientAddress recipient,
        PaymentRefundedEmailModel model,
        CancellationToken ct = default)
    {
        var subject = $"Refund processed for order #{model.OrderId}";
        var htmlBody = await _templateRenderer.RenderAsync(
            "payment-refunded",
            new Dictionary<string, string>
            {
                ["OrderId"] = model.OrderId.ToString(),
                ["CustomerName"] = model.CustomerName,
                ["Amount"] = model.Amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                ["Currency"] = model.Currency,
                ["RefundedAt"] = model.RefundedAt.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                ["SupportEmail"] = model.SupportEmail
            },
            ct);

        var message = BuildMessage(recipient, subject, htmlBody);
        await SendAsync(message, ct);
    }

    public async Task SendOrderShippedAsync(
        RecipientAddress recipient,
        OrderShippedEmailModel model,
        CancellationToken ct = default)
    {
        var subject = $"Your order #{model.OrderId} has shipped";
        var htmlBody = await _templateRenderer.RenderAsync(
            "order-shipped",
            new Dictionary<string, string>
            {
                ["OrderId"] = model.OrderId.ToString(),
                ["CustomerName"] = model.CustomerName,
                ["TrackingNumber"] = model.TrackingNumber ?? "N/A",
                ["EstimatedDelivery"] = model.EstimatedDelivery
            },
            ct);

        var message = BuildMessage(recipient, subject, htmlBody);
        await SendAsync(message, ct);
    }

    public async Task SendPaymentFailedAsync(
        RecipientAddress recipient,
        PaymentFailedEmailModel model,
        CancellationToken ct = default)
    {
        var subject = $"Payment failed for order #{model.OrderId}";
        var htmlBody = await _templateRenderer.RenderAsync(
            "payment-failed",
            new Dictionary<string, string>
            {
                ["OrderId"] = model.OrderId.ToString(),
                ["CustomerName"] = model.CustomerName,
                ["FailureReason"] = model.FailureReason,
                ["SupportEmail"] = model.SupportEmail
            },
            ct);

        var message = BuildMessage(recipient, subject, htmlBody);
        await SendAsync(message, ct);
    }

    private MimeMessage BuildMessage(RecipientAddress recipient, string subject, string htmlBody)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_smtpSettings.FromName, _smtpSettings.FromEmail));
        message.To.Add(new MailboxAddress(recipient.DisplayName ?? recipient.Email, recipient.Email));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        return message;
    }

    private async Task SendAsync(MimeMessage message, CancellationToken ct)
    {
        using var smtpClient = new SmtpClient();
        smtpClient.CheckCertificateRevocation = _smtpSettings.CheckCertificateRevocation;

        var secureSocketOptions = _smtpSettings.UseSsl
            ? SecureSocketOptions.StartTls
            : SecureSocketOptions.None;

        await smtpClient.ConnectAsync(_smtpSettings.Host, _smtpSettings.Port, secureSocketOptions, ct);

        if (!string.IsNullOrWhiteSpace(_smtpSettings.Username))
        {
            await smtpClient.AuthenticateAsync(_smtpSettings.Username, _smtpSettings.Password, ct);
        }

        var response = await smtpClient.SendAsync(message, ct);
        await smtpClient.DisconnectAsync(true, ct);

        _logger.LogInformation(
            "Email sent to {Recipient}. Subject={Subject}. ProviderResponse={ProviderResponse}",
            message.To.ToString(),
            message.Subject,
            response);
    }
}
