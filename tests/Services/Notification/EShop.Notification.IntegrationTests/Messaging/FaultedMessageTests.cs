using EShop.BuildingBlocks.Infrastructure.Extensions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Entities;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Domain.Models;
using EShop.Notification.Domain.ValueObjects;
using EShop.Notification.Infrastructure.Configuration;
using EShop.Notification.Infrastructure.Data;
using EShop.Notification.Infrastructure.Extensions;
using EShop.Notification.Infrastructure.Repositories;
using EShop.Notification.IntegrationTests.Fixtures;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EShop.Notification.IntegrationTests.Messaging;

/// <summary>
/// Notification audit S3 (M5, D6). Through the real consumer registration (<c>AddNotificationConsumers</c>) and the
/// production endpoint names: a password-reset message whose delivery fails is discarded, so its live reset token is
/// never parked in <c>notification_password_reset_requested_error</c>; an order confirmation that fails the same way
/// still reaches its error queue, where it can be replayed. The harness bus has no retry policy, so the first failure
/// is the final one.
/// </summary>
[TestFixture]
public class FaultedMessageTests
{
    [Test]
    public async Task AFailedPasswordResetMessage_IsDiscarded_WhileAFailedOrderConfirmationIsParked()
    {
        var connectionString = await PostgresTestServer.CreateDatabaseAsync();
        var parkedResets = 0;
        var parkedOrders = 0;

        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(TimeProvider.System);
            services.AddSingleton(Options.Create(new SmtpSettings { FromEmail = "support@eshop.local" }));
            services.AddSingleton(Options.Create(new PasswordResetSettings { ResetUrlBase = "https://frontend/reset-password" }));
            services.AddDbContext<NotificationDbContext>(o => o.UseNpgsql(connectionString));
            services.AddScoped<INotificationLogRepository, NotificationLogRepository>();
            services.AddScoped<IUserContactResolver, FoundResolver>();
            services.AddScoped<IEmailService, SmtpDownEmailService>();
            services.AddMassTransitTestHarness(cfg =>
            {
                cfg.SetEndpointNameFormatter(MassTransitServiceCollectionExtensions.CreateEndpointNameFormatter("notification"));
                cfg.AddNotificationConsumers();
                cfg.UsingInMemory((context, bus) =>
                {
                    // Stand-ins for the error queues. Without ConfigureConsumeTopology = false each would also subscribe
                    // to its message type and receive every published copy, not only what the error transport moves.
                    bus.ReceiveEndpoint("notification_password_reset_requested_error", e =>
                    {
                        e.ConfigureConsumeTopology = false;
                        e.Handler<PasswordResetRequestedIntegrationEvent>(_ =>
                        {
                            Interlocked.Increment(ref parkedResets);
                            return Task.CompletedTask;
                        });
                    });
                    bus.ReceiveEndpoint("notification_order_created_error", e =>
                    {
                        e.ConfigureConsumeTopology = false;
                        e.Handler<OrderCreatedEvent>(_ =>
                        {
                            Interlocked.Increment(ref parkedOrders);
                            return Task.CompletedTask;
                        });
                    });
                    bus.ConfigureEndpoints(context);
                });
            });

            await using var provider = services.BuildServiceProvider(true);
            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();

            try
            {
                var reset = new PasswordResetRequestedIntegrationEvent
                {
                    EventId = Guid.NewGuid(), UserId = "user-1", ResetToken = "live-reset-token"
                };
                var order = new OrderCreatedEvent
                {
                    EventId = Guid.NewGuid(), OrderId = Guid.NewGuid(), UserId = "user-1", TotalAmount = 1m
                };

                await harness.Bus.Publish(reset);
                await harness.Bus.Publish(order);

                Assert.That(await harness.Published.Any<Fault<PasswordResetRequestedIntegrationEvent>>(), Is.True,
                    "precondition: the reset delivery failed");
                Assert.That(await harness.Published.Any<Fault<OrderCreatedEvent>>(), Is.True,
                    "precondition: the order confirmation failed");
                await harness.InactivityTask;

                await using var scope = provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
                var resetLog = await db.NotificationLogs.AsNoTracking().SingleAsync(l => l.EventId == reset.EventId);

                Assert.Multiple(() =>
                {
                    Assert.That(parkedResets, Is.Zero, "the reset token is not parked in the error queue (D6)");
                    Assert.That(parkedOrders, Is.EqualTo(1), "control: other notifications keep their error queue");
                    Assert.That(resetLog.Status, Is.EqualTo(NotificationStatus.Failed), "the log still records the failure");
                });
            }
            finally
            {
                await harness.Stop();
            }
        }
        finally
        {
            PostgresTestServer.ReleaseDatabase(connectionString);
        }
    }

    private sealed class FoundResolver : IUserContactResolver
    {
        public Task<RecipientLookup> ResolveAsync(string userId, CancellationToken ct = default)
            => Task.FromResult(RecipientLookup.Found(new RecipientAddress("user@test.com", "User")));
    }

    /// <summary>Every send fails, as an SMTP outage would.</summary>
    private sealed class SmtpDownEmailService : IEmailService
    {
        private static Task<string> Down() => throw new InvalidOperationException("Simulated SMTP outage");

        public Task<string> SendOrderConfirmationAsync(RecipientAddress recipient, OrderConfirmationEmailModel model, CancellationToken ct = default) => Down();
        public Task<string> SendOrderShippedAsync(RecipientAddress recipient, OrderShippedEmailModel model, CancellationToken ct = default) => Down();
        public Task<string> SendPaymentCreatedAsync(RecipientAddress recipient, PaymentCreatedEmailModel model, CancellationToken ct = default) => Down();
        public Task<string> SendPaymentCompletedAsync(RecipientAddress recipient, PaymentCompletedEmailModel model, CancellationToken ct = default) => Down();
        public Task<string> SendPaymentFailedAsync(RecipientAddress recipient, PaymentFailedEmailModel model, CancellationToken ct = default) => Down();
        public Task<string> SendPaymentRefundedAsync(RecipientAddress recipient, PaymentRefundedEmailModel model, CancellationToken ct = default) => Down();
        public Task<string> SendPasswordResetAsync(RecipientAddress recipient, PasswordResetEmailModel model, CancellationToken ct = default) => Down();
    }
}
