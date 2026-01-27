using EShop.Notification.Domain.Interfaces;
using MailKit.Net.Smtp;
using MimeKit;

namespace EShop.Notification.Infrastructure.Services;

/// <summary>
/// Email service using MailKit (SMTP)
/// </summary>
public class EmailService : IEmailService
{
    // TODO: Inject IConfiguration, ILogger, ITemplateRenderer
    // private readonly SmtpSettings _smtpSettings;
    // private readonly ILogger<EmailService> _logger;

    public async Task SendOrderConfirmationAsync(
        OrderConfirmationEmail email, 
        CancellationToken cancellationToken = default)
    {
        // TODO: Render email template with order data
        // TODO: Create MimeMessage
        // TODO: Send via SMTP
        // TODO: Log success/failure
        throw new NotImplementedException();
    }

    public async Task SendOrderShippedAsync(
        OrderShippedEmail email, 
        CancellationToken cancellationToken = default)
    {
        // TODO: Render shipping email template
        // TODO: Include tracking number
        // TODO: Send email
        throw new NotImplementedException();
    }

    public async Task SendPaymentFailedAsync(
        PaymentFailedEmail email, 
        CancellationToken cancellationToken = default)
    {
        // TODO: Render payment failure email template
        // TODO: Include failure reason
        // TODO: Send email
        throw new NotImplementedException();
    }

    public async Task SendWelcomeEmailAsync(
        WelcomeEmail email, 
        CancellationToken cancellationToken = default)
    {
        // TODO: Render welcome email template
        // TODO: Send email
        throw new NotImplementedException();
    }

    // TODO: Add private helper method for sending emails
    // private async Task SendEmailAsync(string to, string subject, string htmlBody)
}
