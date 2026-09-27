using System.Reflection;
using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.Notification.Application.Notifications.Commands.MarkNotificationUndeliverable;
using EShop.Notification.Application.Notifications.Commands.RetryFailedNotifications;
using EShop.Notification.Application.Notifications.Commands.SendTestNotification;
using EShop.Notification.Domain.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EShop.Notification.UnitTests.Commands;

/// <summary>Admin panel S13. The operator actions' validators and request shapes, and the batch retry's per-row report.</summary>
[TestFixture]
public class NotificationActionCommandTests
{
    // ---------- mark-undeliverable ----------

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void MarkUndeliverable_RequiresAReason(string? reason)
    {
        var result = new MarkNotificationUndeliverableCommandValidator()
            .Validate(new MarkNotificationUndeliverableCommand(Guid.NewGuid(), reason));

        Assert.That(result.Errors.Select(e => e.PropertyName), Does.Contain("Reason"));
    }

    [Test]
    public void MarkUndeliverable_CapsTheReason()
    {
        var validator = new MarkNotificationUndeliverableCommandValidator();
        var max = MarkNotificationUndeliverableCommand.MaxReasonLength;

        Assert.Multiple(() =>
        {
            Assert.That(validator.Validate(new MarkNotificationUndeliverableCommand(Guid.NewGuid(), new string('x', max))).IsValid,
                Is.True);
            Assert.That(validator.Validate(new MarkNotificationUndeliverableCommand(Guid.NewGuid(), new string('x', max + 1))).IsValid,
                Is.False);
        });
    }

    // ---------- retry-failed ----------

    [TestCase(null, true)]
    [TestCase(1, true)]
    [TestCase(RetryFailedNotificationsCommand.MaxRetryPerRequest, true)]
    [TestCase(RetryFailedNotificationsCommand.MaxRetryPerRequest + 1, false)]
    [TestCase(0, false)]
    [TestCase(-1, false)]
    public void RetryFailed_IsBounded(int? limit, bool valid)
    {
        var result = new RetryFailedNotificationsCommandValidator().Validate(new RetryFailedNotificationsCommand { Limit = limit });

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.EqualTo(valid));
            if (!valid)
            {
                Assert.That(result.Errors.Select(e => e.PropertyName), Does.Contain("Limit"),
                    "named as the caller sent it, not as the EffectiveLimit accessor nobody can set");
            }
        });
    }

    [Test]
    public void RetryFailed_WithoutALimit_TakesTheCap()
    {
        Assert.That(new RetryFailedNotificationsCommand().EffectiveLimit, Is.EqualTo(RetryFailedNotificationsCommand.MaxRetryPerRequest));
    }

    [Test]
    public void RetryFailed_RefusesADateWindowThatEndsBeforeItStarts()
    {
        var result = new RetryFailedNotificationsCommandValidator().Validate(new RetryFailedNotificationsCommand
        {
            From = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc),
            To = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)
        });

        Assert.That(result.Errors.Select(e => e.PropertyName), Does.Contain("To"));
    }

    [Test]
    public void RetryFailed_ReadsADateWithNoTimeZoneAsUtc_AndRetriesOnlyFailedRows()
    {
        // A JSON body's "2026-09-01" binds as Kind.Unspecified, the same trap as the query string's: Npgsql refuses it.
        var filter = new RetryFailedNotificationsCommand
        {
            From = new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Unspecified)
        }.ToFilter();

        Assert.Multiple(() =>
        {
            Assert.That(filter.From!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(filter.From!.Value, Is.EqualTo(new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Utc)));
            Assert.That(filter.Statuses, Is.Empty, "the query service imposes Failed itself; the command carries none");
        });
    }

    /// <summary>
    /// Each row is dispatched on its own: one the bus refuses is reported, and the rows after it still go. A batch that
    /// stopped at the first refusal would report the rest as neither sent nor failed.
    /// </summary>
    [Test]
    public async Task RetryFailed_ReportsEachRow_AndOneRefusalDoesNotStopTheRest()
    {
        var first = new NotificationRetryCandidate(Guid.NewGuid(), "OrderCreatedEvent", "{\"a\":1}");
        var refused = new NotificationRetryCandidate(Guid.NewGuid(), "OrderCreatedEvent", "{\"a\":2}");
        var last = new NotificationRetryCandidate(Guid.NewGuid(), "PaymentFailedEvent", "{\"a\":3}");

        var queries = new Mock<INotificationQueryService>();
        queries.Setup(q => q.GetRetryCandidatesAsync(It.IsAny<NotificationJournalFilter>(), 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<NotificationRetryCandidate>)[first, refused, last], 7));

        var redispatcher = new Mock<INotificationRedispatcher>();
        redispatcher.SetupGet(r => r.IsAvailable).Returns(true);
        redispatcher.Setup(r => r.CanRedispatch(It.IsAny<string>())).Returns(true);
        redispatcher.Setup(r => r.RedispatchAsync(It.IsAny<string>(), refused.Payload, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("broker refused"));

        var handler = new RetryFailedNotificationsCommandHandler(
            queries.Object, redispatcher.Object, Mock.Of<ICurrentUserContext>(),
            NullLogger<RetryFailedNotificationsCommandHandler>.Instance);

        var result = await handler.Handle(new RetryFailedNotificationsCommand(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value!.DispatchedIds, Is.EqualTo(new[] { first.Id, last.Id }));
            Assert.That(result.Value.FailedIds, Is.EqualTo(new[] { refused.Id }));
            Assert.That(result.Value.Matching, Is.EqualTo(7), "more matched than one batch takes: call again");
        });
        redispatcher.Verify(r => r.RedispatchAsync(last.EventType, last.Payload, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------- test send ----------

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not-an-address")]
    [TestCase("two@@at.test")]
    public void TestSend_RequiresAnAddressTheSenderCanParse(string? email)
    {
        var result = new SendTestNotificationCommandValidator()
            .Validate(new SendTestNotificationCommand { TemplateName = "order-created", Email = email });

        Assert.That(result.Errors.Select(e => e.PropertyName), Does.Contain("Email"));
    }

    [Test]
    public void TestSend_AcceptsAPlainAddress_WithOrWithoutAName()
    {
        var validator = new SendTestNotificationCommandValidator();

        Assert.Multiple(() =>
        {
            Assert.That(validator.Validate(new SendTestNotificationCommand { Email = "ops@eshop.test" }).IsValid, Is.True);
            Assert.That(validator.Validate(new SendTestNotificationCommand { Email = "ops@eshop.test", Name = "Ops" }).IsValid,
                Is.True);
        });
    }

    // ---------- request logging ----------

    /// <summary>
    /// LoggingBehavior logs the whole request at Information, and its name-based redaction list does not contain
    /// <c>Email</c>. Only the attribute keeps an address out of Seq.
    /// </summary>
    [TestCase(typeof(RetryFailedNotificationsCommand))]
    [TestCase(typeof(SendTestNotificationCommand))]
    public void AnAddressInACommand_IsMarkedSensitive(Type command)
    {
        var property = command.GetProperty("Email")!;

        Assert.That(property.GetCustomAttribute<SensitiveDataAttribute>(), Is.Not.Null);
    }
}
