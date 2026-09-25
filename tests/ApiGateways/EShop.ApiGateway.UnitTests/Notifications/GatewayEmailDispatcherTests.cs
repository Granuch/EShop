using EShop.ApiGateway.Configuration;
using EShop.ApiGateway.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.UnitTests.Notifications;

/// <summary>
/// Frontend-contracts F-55. The gateway's operational notices go to the configured operators and never to the user
/// whose request caused them. The dispatcher used to send each notice to that user — the address from their token, or
/// else from Identity's contact endpoint — so a customer received a "[Gateway] CriticalOperationCompleted" email for
/// every payment call.
/// </summary>
[TestFixture]
public sealed class GatewayEmailDispatcherTests
{
    private static EmailNotificationContext NoticeCausedByACustomer() => new(
        EventType: "DownstreamFailure",
        Route: "payments-route",
        StatusCode: 500,
        Path: "/api/v1/payments/create-intent",
        UserId: "customer-1",
        UserEmail: "customer@example.com",
        CorrelationId: "c1",
        OccurredAtUtc: DateTime.UtcNow);

    /// <summary>Dispatches one notice and returns once <paramref name="expectedSends"/> emails went out, or — when none
    /// are expected — once the notice was taken off the queue and a grace period passed without a send.</summary>
    private static async Task<RecordingSender> DispatchAsync(string[] recipients, int expectedSends)
    {
        var queue = new GatewayEmailQueue();
        var sender = new RecordingSender();
        using var provider = new ServiceCollection()
            .AddSingleton<IEmailSender>(sender)
            .AddSingleton<IEmailTemplateEngine, StubTemplateEngine>()
            .BuildServiceProvider();
        using var dispatcher = new GatewayEmailDispatcher(
            queue,
            provider,
            Options.Create(new GatewayOptions { OperationsEmailRecipients = recipients }),
            NullLogger<GatewayEmailDispatcher>.Instance);

        await dispatcher.StartAsync(CancellationToken.None);
        try
        {
            await queue.EnqueueAsync(NoticeCausedByACustomer(), CancellationToken.None);

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline
                   && (queue.GetSnapshot().DequeuedCount < 1 || sender.Recipients.Count < expectedSends))
            {
                await Task.Delay(20);
            }

            // A wrong send would follow the dequeue within milliseconds; give it the chance to show up.
            await Task.Delay(200);
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
        }

        return sender;
    }

    [Test]
    public async Task ANotice_GoesToEveryConfiguredOperator_AndNeverToTheCaller()
    {
        var sender = await DispatchAsync(["ops@eshop.local", " oncall@eshop.local ", "OPS@eshop.local", ""], 2);

        Assert.That(sender.Recipients, Is.EquivalentTo(new[] { "ops@eshop.local", "oncall@eshop.local" }));
        Assert.That(sender.Recipients, Does.Not.Contain("customer@example.com"));
    }

    [Test]
    public async Task WithNoOperatorConfigured_NothingIsSent_EvenThoughTheCallerHasAnAddress()
    {
        var sender = await DispatchAsync([], 0);

        Assert.That(sender.Recipients, Is.Empty);
    }

    private sealed class RecordingSender : IEmailSender
    {
        private readonly List<string> _recipients = [];

        public IReadOnlyList<string> Recipients
        {
            get
            {
                lock (_recipients)
                {
                    return _recipients.ToList();
                }
            }
        }

        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
        {
            lock (_recipients)
            {
                _recipients.Add(to);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class StubTemplateEngine : IEmailTemplateEngine
    {
        public (string Subject, string HtmlBody) Render(EmailNotificationContext context)
            => ($"[Gateway] {context.EventType} on {context.Route}", "<p>notice</p>");
    }
}
