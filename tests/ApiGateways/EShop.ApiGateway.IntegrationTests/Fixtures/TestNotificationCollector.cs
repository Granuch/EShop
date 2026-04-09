using EShop.ApiGateway.Notifications;

namespace EShop.ApiGateway.IntegrationTests.Fixtures;

public sealed class TestNotificationCollector : IEmailNotificationService
{
    private readonly List<EmailNotificationContext> _items = [];
    private readonly object _sync = new();

    public IReadOnlyList<EmailNotificationContext> Items
    {
        get
        {
            lock (_sync)
            {
                return _items.ToArray();
            }
        }
    }

    public Task QueueAsync(EmailNotificationContext context, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _items.Add(context);
        }

        return Task.CompletedTask;
    }
}
