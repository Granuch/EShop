using EShop.BuildingBlocks.Messaging.Events;
using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Entities;
using EShop.Notification.Domain.Interfaces;
using EShop.Notification.Domain.Models;
using EShop.Notification.Domain.ValueObjects;
using EShop.Notification.Infrastructure.Configuration;
using EShop.Notification.Infrastructure.Consumers;
using EShop.Notification.Infrastructure.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

        var smtpSettings = Options.Create(new SmtpSettings
        {
            FromEmail = "support@eshop.local"
        });

        var consumer = new PaymentFailedConsumer(
            dbContext,
            repo.Object,
            emailService.Object,
            resolver.Object,
            smtpSettings,
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

    [Test]
    public async Task PaymentRefundedConsumer_WhenRecipientResolved_ShouldSendAndMarkSent()
    {
        await using var dbContext = CreateDbContext();
        var repo = new Mock<INotificationLogRepository>();
        var emailService = new Mock<IEmailService>();
        var resolver = new Mock<IUserContactResolver>();

        var evt = new PaymentRefundedEvent
        {
            EventId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid().ToString(),
            OrderId = Guid.NewGuid(),
            UserId = "user-5",
            PaymentIntentId = "pi_ref_1",
            Amount = 42.5m,
            RefundedAt = DateTime.UtcNow
        };

        repo.Setup(x => x.FindByEventIdAsync(evt.EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationLog?)null);

        resolver.Setup(x => x.ResolveAsync(evt.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecipientAddress("user5@test.com", "User Five"));

        emailService.Setup(x => x.SendPaymentRefundedAsync(
                It.IsAny<RecipientAddress>(),
                It.IsAny<PaymentRefundedEmailModel>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var smtpSettings = Options.Create(new SmtpSettings
        {
            FromEmail = "support@eshop.local"
        });

        var consumer = new PaymentRefundedConsumer(
            dbContext,
            repo.Object,
            emailService.Object,
            resolver.Object,
            smtpSettings,
            Mock.Of<ILogger<PaymentRefundedConsumer>>());

        var context = new Mock<ConsumeContext<PaymentRefundedEvent>>();
        context.SetupGet(x => x.Message).Returns(evt);
        context.SetupGet(x => x.CorrelationId).Returns(Guid.Parse(evt.CorrelationId));
        context.SetupGet(x => x.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(context.Object);

        emailService.Verify(x => x.SendPaymentRefundedAsync(
            It.IsAny<RecipientAddress>(),
            It.IsAny<PaymentRefundedEmailModel>(),
            It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(x => x.AddAsync(It.IsAny<NotificationLog>(), It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(x => x.UpdateAsync(It.Is<NotificationLog>(l => l.Status == NotificationStatus.Sent), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task PaymentRefundedConsumer_WhenDuplicateEventId_ShouldSkipSend()
    {
        await using var dbContext = CreateDbContext();
        var repo = new Mock<INotificationLogRepository>();
        var emailService = new Mock<IEmailService>();
        var resolver = new Mock<IUserContactResolver>();

        var evt = new PaymentRefundedEvent
        {
            EventId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "user-6",
            PaymentIntentId = "pi_ref_2",
            Amount = 12m,
            RefundedAt = DateTime.UtcNow
        };

        repo.Setup(x => x.FindByEventIdAsync(evt.EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NotificationLog.CreatePending(evt.EventId, nameof(PaymentRefundedEvent), null, evt.UserId, "dup@test.com", "payment-refunded", "dup"));

        var smtpSettings = Options.Create(new SmtpSettings
        {
            FromEmail = "support@eshop.local"
        });

        var consumer = new PaymentRefundedConsumer(
            dbContext,
            repo.Object,
            emailService.Object,
            resolver.Object,
            smtpSettings,
            Mock.Of<ILogger<PaymentRefundedConsumer>>());

        var context = new Mock<ConsumeContext<PaymentRefundedEvent>>();
        context.SetupGet(x => x.Message).Returns(evt);
        context.SetupGet(x => x.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(context.Object);

        emailService.Verify(x => x.SendPaymentRefundedAsync(
            It.IsAny<RecipientAddress>(),
            It.IsAny<PaymentRefundedEmailModel>(),
            It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(x => x.AddAsync(It.IsAny<NotificationLog>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void PaymentRefundedConsumer_WhenResolverReturnsNull_ShouldMarkFailedAndThrow()
    {
        using var dbContext = CreateDbContext();
        var repo = new Mock<INotificationLogRepository>();
        var emailService = new Mock<IEmailService>();
        var resolver = new Mock<IUserContactResolver>();

        var evt = new PaymentRefundedEvent
        {
            EventId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "user-7",
            PaymentIntentId = "pi_ref_3",
            Amount = 77m,
            RefundedAt = DateTime.UtcNow
        };

        repo.Setup(x => x.FindByEventIdAsync(evt.EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationLog?)null);

        resolver.Setup(x => x.ResolveAsync(evt.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RecipientAddress?)null);

        var smtpSettings = Options.Create(new SmtpSettings
        {
            FromEmail = "support@eshop.local"
        });

        var consumer = new PaymentRefundedConsumer(
            dbContext,
            repo.Object,
            emailService.Object,
            resolver.Object,
            smtpSettings,
            Mock.Of<ILogger<PaymentRefundedConsumer>>());

        var context = new Mock<ConsumeContext<PaymentRefundedEvent>>();
        context.SetupGet(x => x.Message).Returns(evt);
        context.SetupGet(x => x.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        Assert.ThrowsAsync<InvalidOperationException>(() => consumer.Consume(context.Object));

        repo.Verify(x => x.UpdateAsync(It.Is<NotificationLog>(l => l.Status == NotificationStatus.Failed), It.IsAny<CancellationToken>()), Times.Once);
        emailService.Verify(x => x.SendPaymentRefundedAsync(
            It.IsAny<RecipientAddress>(),
            It.IsAny<PaymentRefundedEmailModel>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task PaymentCreatedConsumer_WhenRecipientResolved_ShouldSendAndMarkSent()
    {
        await using var dbContext = CreateDbContext();
        var repo = new Mock<INotificationLogRepository>();
        var emailService = new Mock<IEmailService>();
        var resolver = new Mock<IUserContactResolver>();

        var evt = new PaymentCreatedEvent
        {
            EventId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "user-8",
            Amount = 18m,
            Currency = "USD",
            Status = "PENDING",
            CreatedAt = DateTime.UtcNow
        };

        repo.Setup(x => x.FindByEventIdAsync(evt.EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationLog?)null);

        resolver.Setup(x => x.ResolveAsync(evt.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecipientAddress("user8@test.com", "User Eight"));

        emailService.Setup(x => x.SendPaymentCreatedAsync(
                It.IsAny<RecipientAddress>(),
                It.IsAny<PaymentCreatedEmailModel>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var consumer = new PaymentCreatedConsumer(
            dbContext,
            repo.Object,
            emailService.Object,
            resolver.Object,
            Mock.Of<ILogger<PaymentCreatedConsumer>>());

        var context = new Mock<ConsumeContext<PaymentCreatedEvent>>();
        context.SetupGet(x => x.Message).Returns(evt);
        context.SetupGet(x => x.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(context.Object);

        emailService.Verify(x => x.SendPaymentCreatedAsync(
            It.IsAny<RecipientAddress>(),
            It.IsAny<PaymentCreatedEmailModel>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task PaymentCompletedConsumer_WhenRecipientResolved_ShouldSendAndMarkSent()
    {
        await using var dbContext = CreateDbContext();
        var repo = new Mock<INotificationLogRepository>();
        var emailService = new Mock<IEmailService>();
        var resolver = new Mock<IUserContactResolver>();

        var evt = new PaymentCompletedEvent
        {
            EventId = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "user-9",
            Amount = 19m,
            Currency = "USD",
            PaymentIntentId = "pi_complete",
            CompletedAt = DateTime.UtcNow
        };

        repo.Setup(x => x.FindByEventIdAsync(evt.EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationLog?)null);

        resolver.Setup(x => x.ResolveAsync(evt.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecipientAddress("user9@test.com", "User Nine"));

        emailService.Setup(x => x.SendPaymentCompletedAsync(
                It.IsAny<RecipientAddress>(),
                It.IsAny<PaymentCompletedEmailModel>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var consumer = new PaymentCompletedConsumer(
            dbContext,
            repo.Object,
            emailService.Object,
            resolver.Object,
            Mock.Of<ILogger<PaymentCompletedConsumer>>());

        var context = new Mock<ConsumeContext<PaymentCompletedEvent>>();
        context.SetupGet(x => x.Message).Returns(evt);
        context.SetupGet(x => x.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(context.Object);

        emailService.Verify(x => x.SendPaymentCompletedAsync(
            It.IsAny<RecipientAddress>(),
            It.IsAny<PaymentCompletedEmailModel>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task PasswordResetRequestedConsumer_WhenRecipientResolved_ShouldSendAndMarkSent()
    {
        await using var dbContext = CreateDbContext();
        var repo = new Mock<INotificationLogRepository>();
        var emailService = new Mock<IEmailService>();
        var resolver = new Mock<IUserContactResolver>();

        var evt = new PasswordResetRequestedIntegrationEvent
        {
            EventId = Guid.NewGuid(),
            UserId = "user-reset",
            ResetToken = "token-value"
        };

        repo.Setup(x => x.FindByEventIdAsync(evt.EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationLog?)null);

        resolver.Setup(x => x.ResolveAsync(evt.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecipientAddress("reset@test.com", "Reset User"));

        emailService.Setup(x => x.SendPasswordResetAsync(
                It.IsAny<RecipientAddress>(),
                It.IsAny<PasswordResetEmailModel>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var options = Options.Create(new PasswordResetSettings
        {
            ResetUrlBase = "https://frontend/reset-password"
        });

        var consumer = new PasswordResetRequestedConsumer(
            dbContext,
            repo.Object,
            resolver.Object,
            emailService.Object,
            options,
            Mock.Of<ILogger<PasswordResetRequestedConsumer>>());

        var context = new Mock<ConsumeContext<PasswordResetRequestedIntegrationEvent>>();
        context.SetupGet(x => x.Message).Returns(evt);
        context.SetupGet(x => x.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        await consumer.Consume(context.Object);

        emailService.Verify(x => x.SendPasswordResetAsync(
            It.IsAny<RecipientAddress>(),
            It.Is<PasswordResetEmailModel>(m => m.ResetLink.Contains("userId=user-reset", StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(x => x.UpdateAsync(It.Is<NotificationLog>(l => l.Status == NotificationStatus.Sent), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public void PasswordResetRequestedConsumer_WhenResetUrlIsNotAbsolute_ShouldThrowOnCreation()
    {
        using var dbContext = CreateDbContext();
        var options = Options.Create(new PasswordResetSettings
        {
            ResetUrlBase = "/reset-password"
        });

        Assert.Throws<InvalidOperationException>(() => new PasswordResetRequestedConsumer(
            dbContext,
            Mock.Of<INotificationLogRepository>(),
            Mock.Of<IUserContactResolver>(),
            Mock.Of<IEmailService>(),
            options,
            Mock.Of<ILogger<PasswordResetRequestedConsumer>>()));
    }

    [Test]
    public void PasswordResetRequestedConsumer_WhenEmailSendFails_ShouldStoreSanitizedError()
    {
        using var dbContext = CreateDbContext();
        var repo = new Mock<INotificationLogRepository>();
        var emailService = new Mock<IEmailService>();
        var resolver = new Mock<IUserContactResolver>();

        var evt = new PasswordResetRequestedIntegrationEvent
        {
            EventId = Guid.NewGuid(),
            UserId = "user-reset-failed",
            ResetToken = "token-value"
        };

        repo.Setup(x => x.FindByEventIdAsync(evt.EventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((NotificationLog?)null);

        resolver.Setup(x => x.ResolveAsync(evt.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RecipientAddress("reset@test.com", "Reset User"));

        emailService.Setup(x => x.SendPasswordResetAsync(
                It.IsAny<RecipientAddress>(),
                It.IsAny<PasswordResetEmailModel>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp provider timeout at internal.mailtrap.local:2525"));

        NotificationLog? updatedLog = null;
        repo.Setup(x => x.UpdateAsync(It.IsAny<NotificationLog>(), It.IsAny<CancellationToken>()))
            .Callback<NotificationLog, CancellationToken>((log, _) => updatedLog = log)
            .Returns(Task.CompletedTask);

        var options = Options.Create(new PasswordResetSettings
        {
            ResetUrlBase = "https://frontend/reset-password"
        });

        var consumer = new PasswordResetRequestedConsumer(
            dbContext,
            repo.Object,
            resolver.Object,
            emailService.Object,
            options,
            Mock.Of<ILogger<PasswordResetRequestedConsumer>>());

        var context = new Mock<ConsumeContext<PasswordResetRequestedIntegrationEvent>>();
        context.SetupGet(x => x.Message).Returns(evt);
        context.SetupGet(x => x.MessageId).Returns(Guid.NewGuid());
        context.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);

        Assert.ThrowsAsync<InvalidOperationException>(() => consumer.Consume(context.Object));
        Assert.That(updatedLog, Is.Not.Null);
        Assert.That(updatedLog!.Status, Is.EqualTo(NotificationStatus.Failed));
        Assert.That(updatedLog.LastError, Is.EqualTo("Email provider timeout."));
    }
}
