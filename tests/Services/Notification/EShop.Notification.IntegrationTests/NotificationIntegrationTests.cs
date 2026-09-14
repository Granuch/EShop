using EShop.BuildingBlocks.Messaging.Events;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Domain.Models;
using EShop.Notification.Domain.ValueObjects;
using EShop.Notification.Infrastructure.Consumers;
using EShop.Notification.Infrastructure.Data;
using EShop.Notification.Infrastructure.Repositories;
using EShop.Notification.IntegrationTests.Fixtures;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Notification.IntegrationTests;

/// <summary>
/// The failure-path and duplicate tests that used to live here ran on EF InMemory and asserted a persisted <c>Failed</c>
/// row that PostgreSQL rolled back (Notification audit H2). They were replaced by
/// <c>Persistence/ConsumerDeliveryRecordTests</c>, which run on a real database; so, since S2, were the two InMemory
/// success-path tests, which the unit tests also cover.
/// </summary>
[TestFixture]
public class NotificationIntegrationTests
{
    [Test]
    public async Task HostBootsInTestingEnvironment()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));

        var client = factory.CreateClient();
        var response = await client.GetAsync("/health/live");

        Assert.That((int)response.StatusCode, Is.EqualTo(200));
    }

    /// <summary>On PostgreSQL, through the bus and the container's own registrations.</summary>
    [Test]
    public async Task OrderCreatedConsumer_FromHarness_PersistsSentLog()
    {
        var connectionString = await PostgresTestServer.CreateDatabaseAsync();
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(TimeProvider.System);
            services.AddDbContext<NotificationDbContext>(o => o.UseNpgsql(connectionString));
            services.AddScoped<INotificationLogRepository, NotificationLogRepository>();
            services.AddScoped<IUserContactResolver>(_ => new StubUserResolver(new RecipientAddress("user@test.com", "User")));
            services.AddScoped<IEmailService>(_ => new StubEmailService());
            services.AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<OrderCreatedConsumer>();
                cfg.UsingInMemory((context, busCfg) =>
                {
                    busCfg.ConfigureEndpoints(context);
                });
            });

            await using var provider = services.BuildServiceProvider(true);
            var harness = provider.GetRequiredService<ITestHarness>();
            await harness.Start();

            try
            {
                var eventId = Guid.NewGuid();

                await harness.Bus.Publish(new OrderCreatedEvent
                {
                    EventId = eventId,
                    OrderId = Guid.NewGuid(),
                    UserId = "user-1",
                    TotalAmount = 120.0m
                });

                Assert.That(await harness.Consumed.Any<OrderCreatedEvent>(), Is.True);
                var consumerHarness = harness.GetConsumerHarness<OrderCreatedConsumer>();
                Assert.That(await consumerHarness.Consumed.Any<OrderCreatedEvent>(), Is.True);

                await using var scope = provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
                var log = await db.NotificationLogs.FirstOrDefaultAsync(x => x.EventId == eventId);

                Assert.That(log, Is.Not.Null);
                Assert.That(log!.Status, Is.EqualTo(EShop.Notification.Domain.Entities.NotificationStatus.Sent));
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

    private sealed class StubUserResolver : IUserContactResolver
    {
        private readonly RecipientAddress? _recipient;

        public StubUserResolver(RecipientAddress? recipient)
        {
            _recipient = recipient;
        }

        public Task<RecipientAddress?> ResolveAsync(string userId, CancellationToken ct = default)
        {
            return Task.FromResult(_recipient);
        }
    }

    private sealed class StubEmailService : IEmailService
    {
        public Task SendOrderConfirmationAsync(RecipientAddress recipient, OrderConfirmationEmailModel model, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendOrderShippedAsync(RecipientAddress recipient, OrderShippedEmailModel model, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendPaymentCreatedAsync(RecipientAddress recipient, PaymentCreatedEmailModel model, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendPaymentCompletedAsync(RecipientAddress recipient, PaymentCompletedEmailModel model, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendPaymentFailedAsync(RecipientAddress recipient, PaymentFailedEmailModel model, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendPaymentRefundedAsync(RecipientAddress recipient, PaymentRefundedEmailModel model, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SendPasswordResetAsync(RecipientAddress recipient, PasswordResetEmailModel model, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
