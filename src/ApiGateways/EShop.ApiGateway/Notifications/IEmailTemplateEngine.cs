namespace EShop.ApiGateway.Notifications;

public interface IEmailTemplateEngine
{
    (string Subject, string HtmlBody) Render(EmailNotificationContext context);
}
