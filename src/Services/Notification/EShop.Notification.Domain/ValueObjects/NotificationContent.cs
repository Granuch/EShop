namespace EShop.Notification.Domain.ValueObjects;

public sealed record NotificationContent
{
    public NotificationContent(string subject, string htmlBody, string templateName)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("Subject is required.", nameof(subject));
        }

        if (string.IsNullOrWhiteSpace(htmlBody))
        {
            throw new ArgumentException("HtmlBody is required.", nameof(htmlBody));
        }

        if (string.IsNullOrWhiteSpace(templateName))
        {
            throw new ArgumentException("TemplateName is required.", nameof(templateName));
        }

        Subject = subject;
        HtmlBody = htmlBody;
        TemplateName = templateName;
    }

    public string Subject { get; }
    public string HtmlBody { get; }
    public string TemplateName { get; }
}
