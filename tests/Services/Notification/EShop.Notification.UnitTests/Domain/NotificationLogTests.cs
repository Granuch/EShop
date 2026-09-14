using EShop.Notification.Domain.Entities;

namespace EShop.Notification.UnitTests.Domain;

/// <summary>Notification audit S2 (D5): the state machine behind the delivery claim.</summary>
[TestFixture]
public class NotificationLogTests
{
    private static readonly DateTime Now = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

    [Test]
    public void AnAttemptWithinTheLease_IsInProgress()
    {
        var log = Pending();
        log.BeginAttempt(Now);

        Assert.Multiple(() =>
        {
            Assert.That(log.IsAttemptInProgress(Now + Lease - TimeSpan.FromSeconds(1), Lease), Is.True);
            Assert.That(log.IsAttemptInProgress(Now + Lease, Lease), Is.False, "at the lease it is taken to be dead");
        });
    }

    [Test]
    public void OnlyASendingRow_HoldsAnAttempt()
    {
        var failed = Pending();
        failed.BeginAttempt(Now);
        failed.MarkFailed("Simulated SMTP failure");

        Assert.Multiple(() =>
        {
            Assert.That(Pending().IsAttemptInProgress(Now, Lease), Is.False);
            Assert.That(failed.IsAttemptInProgress(Now, Lease), Is.False);
        });
    }

    [Test]
    public void ASentNotification_IsNeverAttemptedAgain_NorFailed()
    {
        var log = Pending();
        log.BeginAttempt(Now);
        log.MarkSent(providerMessageId: null);

        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => log.BeginAttempt(Now));
            Assert.Throws<InvalidOperationException>(() => log.MarkFailed("late failure"));
        });
    }

    [Test]
    public void OnlyAnAttemptInProgress_CanBeMarkedSent()
    {
        var log = Pending();

        Assert.Throws<InvalidOperationException>(() => log.MarkSent(providerMessageId: null));
    }

    [Test]
    public void EachFailedAttempt_IsCounted()
    {
        var log = Pending();
        log.BeginAttempt(Now);
        log.MarkFailed("first");
        log.BeginAttempt(Now);
        log.MarkFailed("second");

        Assert.Multiple(() =>
        {
            Assert.That(log.RetryCount, Is.EqualTo(2));
            Assert.That(log.Status, Is.EqualTo(NotificationStatus.Failed));
        });
    }

    /// <summary>S3 (D3).</summary>
    [Test]
    public void AnUndeliverableNotification_IsFinal_Uncounted_AndKeepsItsReasonAsWritten()
    {
        var log = Pending();
        log.BeginAttempt(Now);
        log.MarkUndeliverable("Identity has no such user (404).");

        Assert.Multiple(() =>
        {
            Assert.That(log.IsFinal, Is.True);
            Assert.That(log.RetryCount, Is.Zero);
            Assert.That(log.LastError, Is.EqualTo("Identity has no such user (404)."), "not rewritten by SanitizeError");
            Assert.Throws<InvalidOperationException>(() => log.BeginAttempt(Now));
            Assert.Throws<InvalidOperationException>(() => log.MarkFailed("late failure"));
        });
    }

    [Test]
    public void OnlyAnAttemptInProgress_CanEndUndeliverable()
    {
        Assert.Throws<InvalidOperationException>(() => Pending().MarkUndeliverable("no such user"));
    }

    private static NotificationLog Pending()
        => NotificationLog.CreatePending(Guid.NewGuid(), "Event", null, "user", "template", "subject");
}
