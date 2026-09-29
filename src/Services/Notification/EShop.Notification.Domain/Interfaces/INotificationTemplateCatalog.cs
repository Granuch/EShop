using EShop.Notification.Domain.ValueObjects;

namespace EShop.Notification.Domain.Interfaces;

/// <summary>One email template (Admin panel S13, endpoint #75).</summary>
/// <param name="Name">The template's name, as <c>NotificationLog.TemplateName</c> records it.</param>
/// <param name="EventType">The integration event whose consumer sends it, as <c>NotificationLog.EventType</c> records it.</param>
/// <param name="Resendable">
/// Whether a notification sent with it can be resent. False only for the password reset, whose event is never kept
/// because it carries a live reset token.
/// </param>
public sealed record NotificationTemplateInfo(string Name, string EventType, bool Resendable);

/// <summary>
/// The email templates, and a test send of any one of them with sample data (Admin panel S13, endpoints #75/#76). In
/// Domain because Application calls it and Infrastructure owns the templates and the SMTP client.
/// </summary>
public interface INotificationTemplateCatalog
{
    IReadOnlyList<NotificationTemplateInfo> Templates { get; }

    /// <summary>
    /// Renders <paramref name="templateName"/> with sample data and sends it to <paramref name="recipient"/>, through the
    /// same renderer and SMTP client a real notification uses. Writes no <c>NotificationLog</c> row: a test send
    /// belongs to no event, and the journal is the record of what customers were sent.
    /// </summary>
    /// <returns>The sent message's Message-ID.</returns>
    Task<string> SendTestAsync(string templateName, RecipientAddress recipient, CancellationToken cancellationToken = default);
}
