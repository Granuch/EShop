using EShop.BuildingBlocks.Messaging.Events;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Domain.Models;
using EShop.Notification.Domain.ValueObjects;
using EShop.Notification.Infrastructure.Consumers;
using EShop.Notification.Infrastructure.Data;
using EShop.Notification.Infrastructure.Repositories;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Notification.IntegrationTests;

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

    [Test]
    public async Task OrderCreatedConsumer_FromHarness_PersistsSentLog()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString();
        services.AddLogging();
        services.AddDbContext<NotificationDbContext>(o => o.UseInMemoryDatabase(dbName));
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

    [Test]
    public async Task OrderCreatedConsumer_SuccessPath_CallsEmailService()
    {
        await using var dbContext = CreateDbContext();
        var repository = new NotificationLogRepository(dbContext);
        var resolver = new StubUserResolver(new RecipientAddress("user@test.com", "User"));
        var email = new StubEmailService();

        var consumer = new OrderCreatedConsumer(
            dbContext,
            repository,
            email,
            resolver,
            new LoggerFactory().CreateLogger<OrderCreatedConsumer>());

        var evt = new OrderCreatedEvent
        {
            EventId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "user-10",
            TotalAmount = 44m
        };

        var context = BuildContext(evt, Guid.NewGuid());

        await consumer.Consume(context.Object);

        Assert.That(email.OrderConfirmationCalls, Is.EqualTo(1));
    }

    [Test]
    public void OrderCreatedConsumer_WhenTransientFailureOccurs_IncrementsRetry()
    {
        using var dbContext = CreateDbContext();
        var repository = new NotificationLogRepository(dbContext);
        var resolver = new StubUserResolver(new RecipientAddress("user@test.com", "User"));
        var email = new StubEmailService { ThrowOnOrderConfirmation = true };

        var consumer = new OrderCreatedConsumer(
            dbContext,
            repository,
            email,
            resolver,
            new LoggerFactory().CreateLogger<OrderCreatedConsumer>());

        var eventId = Guid.NewGuid();
        var evt = new OrderCreatedEvent
        {
            EventId = eventId,
            OrderId = Guid.NewGuid(),
            UserId = "user-11",
            TotalAmount = 99m
        };

        var context = BuildContext(evt, Guid.NewGuid());

        Assert.ThrowsAsync<InvalidOperationException>(() => consumer.Consume(context.Object));

        var log = dbContext.NotificationLogs.Single(x => x.EventId == eventId);
        Assert.That(log.RetryCount, Is.EqualTo(1));
        Assert.That(log.Status, Is.EqualTo(EShop.Notification.Domain.Entities.NotificationStatus.Failed));
    }

    [Test]
    public async Task OrderCreatedConsumer_DuplicateMessageId_IsIgnoredByIdempotentBase()
    {
        var dbName = Guid.NewGuid().ToString();
        await using var dbContext = CreateDbContext(dbName);
        var repository = new NotificationLogRepository(dbContext);
        var resolver = new StubUserResolver(new RecipientAddress("user@test.com", "User"));
        var email = new StubEmailService();

        var consumer = new OrderCreatedConsumer(
            dbContext,
            repository,
            email,
            resolver,
            new LoggerFactory().CreateLogger<OrderCreatedConsumer>());

        var messageId = Guid.NewGuid();
        var evt = new OrderCreatedEvent
        {
            EventId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "user-12",
            TotalAmount = 55m
        };

        var first = BuildContext(evt, messageId);
        var second = BuildContext(evt with { EventId = Guid.NewGuid() }, messageId);

        await consumer.Consume(first.Object);

        await using var secondDbContext = CreateDbContext(dbName);
        var secondConsumer = new OrderCreatedConsumer(
            secondDbContext,
            new NotificationLogRepository(secondDbContext),
            email,
            resolver,
            new LoggerFactory().CreateLogger<OrderCreatedConsumer>());

        try
        {
            await secondConsumer.Consume(second.Object);
        }
        catch (Exception)
        {
            // InMemory EF provider can surface duplicate claim handling exceptions differently.
            // The assertion below validates effective idempotent behavior: no duplicate email send.
        }

        Assert.That(email.OrderConfirmationCalls, Is.EqualTo(1));
    }

    private static NotificationDbContext CreateDbContext(string? dbName = null)
    {
        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase(dbName ?? Guid.NewGuid().ToString())
            .Options;

        return new NotificationDbContext(options);
    }

    private static Mock<ConsumeContext<OrderCreatedEvent>> BuildContext(OrderCreatedEvent evt, Guid messageId)
    {
        var context = new Mock<ConsumeContext<OrderCreatedEvent>>();
        context.SetupGet(x => x.Message).Returns(evt);
        context.SetupGet(x => x.MessageId).Returns(messageId);
        context.SetupGet(x => x.CorrelationId).Returns(Guid.NewGuid());
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);
        return context;
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
        public bool ThrowOnOrderConfirmation { get; set; }
        public int OrderConfirmationCalls { get; private set; }

        public Task SendOrderConfirmationAsync(RecipientAddress recipient, OrderConfirmationEmailModel model, CancellationToken ct = default)
        {
            OrderConfirmationCalls++;
            if (ThrowOnOrderConfirmation)
            {
                throw new InvalidOperationException("Simulated SMTP failure");
            }

            return Task.CompletedTask;
        }

        public Task SendOrderShippedAsync(RecipientAddress recipient, OrderShippedEmailModel model, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public Task SendPaymentFailedAsync(RecipientAddress recipient, PaymentFailedEmailModel model, CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }
    }
}
