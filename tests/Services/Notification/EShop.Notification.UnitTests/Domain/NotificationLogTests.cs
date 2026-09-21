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

    // ---------- Admin panel S13: the operator's mark-undeliverable (risk A5) ----------

    [Test]
    public void AnOperator_CanEndAFailedNotification_WithTheirReason_Uncounted()
    {
        var log = Failed();

        log.MarkUndeliverableByOperator("  Mailbox permanently disabled per the provider's bounce report.  ", Now);

        Assert.Multiple(() =>
        {
            Assert.That(log.Status, Is.EqualTo(NotificationStatus.Undeliverable));
            Assert.That(log.IsFinal, Is.True);
            Assert.That(log.LastError, Is.EqualTo(
                NotificationLog.OperatorReasonPrefix + "Mailbox permanently disabled per the provider's bounce report."));
            Assert.That(log.RetryCount, Is.EqualTo(1), "the failed attempt stays counted; the operator's decision is not one");
            Assert.Throws<InvalidOperationException>(() => log.BeginAttempt(Now), "final means never attempted again");
        });
    }

    [Test]
    public void AnOperator_CanEndAPendingNotification()
    {
        var log = Pending();

        log.MarkUndeliverableByOperator("Duplicate of another order's confirmation.", Now);

        Assert.That(log.Status, Is.EqualTo(NotificationStatus.Undeliverable));
    }

    /// <summary>
    /// The delivery path's <c>MarkUndeliverable</c> throws from anything but Sending, which is exactly A5: the operator's
    /// version exists because a Failed row — the one an operator most often wants to close — was refused.
    /// </summary>
    [Test]
    public void TheDeliveryPathsMarkUndeliverable_StillRefusesAFailedRow_WhichIsWhyTheOperatorHasItsOwn()
    {
        Assert.Throws<InvalidOperationException>(() => Failed().MarkUndeliverable("no such user"));
    }

    [Test]
    public void AnOperator_CanEndAnAttemptWhoseLeaseHasExpired()
    {
        var log = Pending();
        log.BeginAttempt(Now - NotificationLog.AttemptLease - TimeSpan.FromSeconds(1));

        log.MarkUndeliverableByOperator("The attempt died with its process.", Now);

        Assert.That(log.Status, Is.EqualTo(NotificationStatus.Undeliverable));
    }

    /// <summary>
    /// A live attempt may already have sent the email; ending it here would make its Sent save fail its row-version check,
    /// which the delivery path swallows (D2) — a row saying Undeliverable for an email the customer has.
    /// </summary>
    [Test]
    public void AnOperator_CannotEndAnAttemptThatStillHoldsItsLease()
    {
        var log = Pending();
        log.BeginAttempt(Now - NotificationLog.AttemptLease + TimeSpan.FromSeconds(1));

        Assert.Throws<InvalidOperationException>(() => log.MarkUndeliverableByOperator("stop it", Now));
        Assert.That(log.Status, Is.EqualTo(NotificationStatus.Sending));
    }

    [Test]
    public void AnOperator_CannotEndANotificationThatIsAlreadyFinal()
    {
        var sent = Pending();
        sent.BeginAttempt(Now);
        sent.MarkSent(providerMessageId: null);

        var undeliverable = Pending();
        undeliverable.BeginAttempt(Now);
        undeliverable.MarkUndeliverable("Identity has no such user (404).");

        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => sent.MarkUndeliverableByOperator("too late", Now));
            Assert.Throws<InvalidOperationException>(() => undeliverable.MarkUndeliverableByOperator("again", Now));
            Assert.That(undeliverable.LastError, Is.EqualTo("Identity has no such user (404)."), "the first reason stands");
        });
    }

    [TestCase("")]
    [TestCase("   ")]
    public void AnOperator_MustGiveAReason(string reason)
    {
        Assert.Throws<ArgumentException>(() => Failed().MarkUndeliverableByOperator(reason, Now));
    }

    // ---------- Admin panel S13: when a resend is refused ----------

    [Test]
    public void AFailedNotificationThatKeptItsEvent_CanBeResent()
    {
        Assert.That(Failed(payload: "{}").ResendBlockerAt(Now), Is.Null);
    }

    [Test]
    public void AFailedNotificationThatKeptNoEvent_CannotBeResent()
    {
        Assert.That(Failed().ResendBlockerAt(Now), Is.EqualTo(NotificationResendBlocker.NoPayload));
    }

    [Test]
    public void AFinalNotification_CannotBeResent_WhateverItKept()
    {
        var sent = Pending(payload: "{}");
        sent.BeginAttempt(Now);
        sent.MarkSent(providerMessageId: null);

        var undeliverable = Failed(payload: "{}");
        undeliverable.MarkUndeliverableByOperator("closed", Now);

        Assert.Multiple(() =>
        {
            Assert.That(sent.ResendBlockerAt(Now), Is.EqualTo(NotificationResendBlocker.Final));
            Assert.That(undeliverable.ResendBlockerAt(Now), Is.EqualTo(NotificationResendBlocker.Final));
        });
    }

    [Test]
    public void AnAttemptHoldingItsLease_BlocksAResend_AndAnExpiredOneDoesNot()
    {
        var live = Pending(payload: "{}");
        live.BeginAttempt(Now - NotificationLog.AttemptLease + TimeSpan.FromSeconds(1));

        var expired = Pending(payload: "{}");
        expired.BeginAttempt(Now - NotificationLog.AttemptLease);

        Assert.Multiple(() =>
        {
            Assert.That(live.ResendBlockerAt(Now), Is.EqualTo(NotificationResendBlocker.AttemptInProgress));
            Assert.That(expired.ResendBlockerAt(Now), Is.Null, "at the lease the attempt is taken to be dead");
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    public void ABlankPayload_IsStoredAsNone(string? payload)
    {
        Assert.That(Pending(payload).Payload, Is.Null);
    }

    [Test]
    public void TheConsumersLease_IsTheDomainsLease()
    {
        // One value for both sides: the operator refuses a live attempt by the rule the delivery path takes a dead one
        // over by. Two constants that drifted apart would open a window where both, or neither, may act.
        Assert.That(
            EShop.Notification.Infrastructure.Consumers.NotificationDelivery.AttemptLease,
            Is.EqualTo(NotificationLog.AttemptLease));
    }

    private static NotificationLog Failed(string? payload = null)
    {
        var log = Pending(payload);
        log.BeginAttempt(Now);
        log.MarkFailed("Simulated SMTP failure");
        return log;
    }

    private static NotificationLog Pending(string? payload = null)
        => NotificationLog.CreatePending(Guid.NewGuid(), "Event", null, "user", "template", "subject", payload);
}
