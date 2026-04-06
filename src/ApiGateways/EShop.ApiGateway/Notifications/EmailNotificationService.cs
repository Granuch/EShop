namespace EShop.ApiGateway.Notifications;

public sealed class EmailNotificationService : IEmailNotificationService
{
    private readonly GatewayEmailQueue _queue;

    public EmailNotificationService(GatewayEmailQueue queue)
    {
        _queue = queue;
    }

    public Task QueueAsync(EmailNotificationContext context, CancellationToken cancellationToken = default)
    {
        return _queue.EnqueueAsync(context, cancellationToken).AsTask();
    }
}
