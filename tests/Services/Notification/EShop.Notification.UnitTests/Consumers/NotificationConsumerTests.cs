using EShop.BuildingBlocks.Messaging.Events;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Entities;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Domain.Models;
using EShop.Notification.Domain.ValueObjects;
using EShop.Notification.Infrastructure.Consumers;
using EShop.Notification.Infrastructure.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Notification.UnitTests.Consumers;

[TestFixture]
public class NotificationConsumerTests
{
    private static NotificationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new NotificationDbContext(options);
    }

    [Test]
    public async Task OrderCreatedConsumer_WhenRecipientResolved_ShouldSendAndMarkSent()
    {
        await using var dbContext = CreateDbContext();
        var repo = new Mock<INotificationLogRepository>();
        var emailService = new Mock<IEmailService>();
        var resolver = new Mock<IUserContactResolver>();

        var evt = new OrderCreatedEvent
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid().ToString(),
            OrderId = Guid.NewGuid(),
            UserId = "user-1",
            TotalAmount = 125.50m
        };

        repo.Setup(x => x.FindByEventIdAsync(evt.EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationLog?)null);

        resolver.Setup(x => x.ResolveAsync(evt.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecipientAddress("user1@test.com", "User One"));

        emailService.Setup(x => x.SendOrderConfirmationAsync(
                It.IsAny<RecipientAddress>(),
                It.IsAny<OrderConfirmationEmailModel>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var consumer = new OrderCreatedConsumer(
            dbContext,
            repo.Object,
            emailService.Object,
            resolver.Object,
            Mock.Of<ILogger<OrderCreatedConsumer>>());

        var context = new Mock<ConsumeContext<OrderCreatedEvent>>();
        context.SetupGet(x => x.Message).Returns(evt);
        context.SetupGet(x => x.CorrelationId).Returns(Guid.Parse(evt.CorrelationId));
        context.SetupGet(x => x.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(context.Object);

        emailService.Verify(x => x.SendOrderConfirmationAsync(
            It.IsAny<RecipientAddress>(),
            It.IsAny<OrderConfirmationEmailModel>(),
            It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(x => x.AddAsync(It.IsAny<NotificationLog>(), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(x => x.UpdateAsync(It.Is<NotificationLog>(l => l.Status == NotificationStatus.Sent), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task OrderCreatedConsumer_WhenDuplicateEventId_ShouldSkipSend()
    {
        await using var dbContext = CreateDbContext();
        var repo = new Mock<INotificationLogRepository>();
        var emailService = new Mock<IEmailService>();
        var resolver = new Mock<IUserContactResolver>();

        var evt = new OrderCreatedEvent
        {
            EventId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "user-2",
            TotalAmount = 10m
        };

        repo.Setup(x => x.FindByEventIdAsync(evt.EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NotificationLog.CreatePending(evt.EventId, nameof(OrderCreatedEvent), null, evt.UserId, "dup@test.com", "order-created", "dup"));

        var consumer = new OrderCreatedConsumer(
            dbContext,
            repo.Object,
            emailService.Object,
            resolver.Object,
            Mock.Of<ILogger<OrderCreatedConsumer>>());

        var context = new Mock<ConsumeContext<OrderCreatedEvent>>();
        context.SetupGet(x => x.Message).Returns(evt);
        context.SetupGet(x => x.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(context.Object);

        emailService.Verify(x => x.SendOrderConfirmationAsync(
            It.IsAny<RecipientAddress>(),
            It.IsAny<OrderConfirmationEmailModel>(),
            It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(x => x.AddAsync(It.IsAny<NotificationLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task OrderShippedConsumer_WhenUserEmailPresent_ShouldBypassResolver()
    {
        await using var dbContext = CreateDbContext();
        var repo = new Mock<INotificationLogRepository>();
        var emailService = new Mock<IEmailService>();
        var resolver = new Mock<IUserContactResolver>();

        var evt = new OrderShippedEvent
        {
            EventId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "user-3",
            UserEmail = "user3@test.com",
            TrackingNumber = "TRK123",
            ShippedAt = DateTime.UtcNow
        };

        repo.Setup(x => x.FindByEventIdAsync(evt.EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationLog?)null);

        emailService.Setup(x => x.SendOrderShippedAsync(
                It.IsAny<RecipientAddress>(),
                It.IsAny<OrderShippedEmailModel>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var consumer = new OrderShippedConsumer(
            dbContext,
            repo.Object,
            emailService.Object,
            resolver.Object,
            Mock.Of<ILogger<OrderShippedConsumer>>());

        var context = new Mock<ConsumeContext<OrderShippedEvent>>();
        context.SetupGet(x => x.Message).Returns(evt);
        context.SetupGet(x => x.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(context.Object);

        resolver.Verify(x => x.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        emailService.Verify(x => x.SendOrderShippedAsync(
            It.IsAny<RecipientAddress>(),
            It.IsAny<OrderShippedEmailModel>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void PaymentFailedConsumer_WhenResolverReturnsNull_ShouldMarkFailedAndThrow()
    {
        using var dbContext = CreateDbContext();
        var repo = new Mock<INotificationLogRepository>();
        var emailService = new Mock<IEmailService>();
        var resolver = new Mock<IUserContactResolver>();

        var evt = new PaymentFailedEvent
        {
            EventId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "user-4",
            Reason = "Card declined",
            FailedAt = DateTime.UtcNow
        };

        repo.Setup(x => x.FindByEventIdAsync(evt.EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationLog?)null);

        resolver.Setup(x => x.ResolveAsync(evt.UserId!, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RecipientAddress?)null);

        var consumer = new PaymentFailedConsumer(
            dbContext,
            repo.Object,
            emailService.Object,
            resolver.Object,
            Mock.Of<ILogger<PaymentFailedConsumer>>());

        var context = new Mock<ConsumeContext<PaymentFailedEvent>>();
        context.SetupGet(x => x.Message).Returns(evt);
        context.SetupGet(x => x.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        Assert.ThrowsAsync<InvalidOperationException>(() => consumer.Consume(context.Object));

        repo.Verify(x => x.UpdateAsync(It.Is<NotificationLog>(l => l.Status == NotificationStatus.Failed), It.IsAny<CancellationToken>()), Times.Once);
        emailService.Verify(x => x.SendPaymentFailedAsync(
            It.IsAny<RecipientAddress>(),
            It.IsAny<PaymentFailedEmailModel>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
