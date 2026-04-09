namespace EShop.ApiGateway.Notifications;

public interface IEmailNotificationService
{
    Task QueueAsync(EmailNotificationContext context, CancellationToken cancellationToken = default);
}
