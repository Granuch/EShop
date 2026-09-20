namespace EShop.Payment.Domain.Entities;

/// <summary>
/// A Stripe webhook delivery this service accepted and then failed to apply (Admin panel S11, endpoint #67).
///
/// <para>
/// <b>Only an internal failure is captured here.</b> A delivery refused for its signature or its payload
/// (<c>StripeWebhookRejectedException</c> → 400) is the sender's fault and redelivering the same bytes can never
/// succeed, so capturing one would fill this table with rows an operator can do nothing about. What is captured is the
/// case Stripe's own redelivery does not reliably cover: our database was down, a constraint fired, a bug threw — the
/// event was good and we lost it.
/// </para>
///
/// <para>
/// <b>The capture must not run inside the failing unit of work.</b> After a failed <c>SaveChanges</c> the DbContext
/// holds the rejected changes and, on Postgres, its transaction is aborted — every later statement on it answers
/// <c>25P02</c>. So <c>IFailedStripeWebhookStore</c> writes through its own scope; a capture written through the
/// failing context would roll back with the very failure it exists to record.
/// </para>
///
/// <para>
/// <b>One row per Stripe event</b>, not one per delivery: Stripe redelivers a failing event for days, and a table with
/// a thousand copies of one incident is unreadable. <see cref="AttemptCount"/>, <see cref="FirstSeenAt"/> and
/// <see cref="LastAttemptAt"/> carry the history instead. The unique index on <see cref="StripeEventId"/> is what
/// enforces it.
/// </para>
/// </summary>
public class FailedStripeWebhook
{
    public const int MaxErrorLength = 2000;
    public const int MaxEventIdLength = 200;
    public const int MaxEventTypeLength = 200;
    public const int MaxSignatureHeaderLength = 500;

    internal FailedStripeWebhook()
    {
    }

    public Guid Id { get; internal set; }

    /// <summary>
    /// Stripe's event id, read from the stored payload. Null only for a payload that reached the internal-failure
    /// branch without one, which should not happen — the parser refuses an event with no id — so a null here is
    /// itself a finding.
    /// </summary>
    public string? StripeEventId { get; internal set; }

    public string? EventType { get; internal set; }

    /// <summary>The delivery's exact bytes. What a replay re-applies, so it is stored verbatim and never re-encoded.</summary>
    public string Payload { get; internal set; } = string.Empty;

    /// <summary>
    /// The <c>Stripe-Signature</c> header of the delivery, for forensics only.
    /// <para><b>A replay does not and cannot use it.</b> Stripe's signature carries a timestamp and the parser refuses
    /// anything signed more than 300 seconds ago, so by the time an operator replays a capture the header is always
    /// expired. Replay is safe without it for a different reason: a row only exists here because the delivery already
    /// passed verification. See <c>IStripeWebhookEventParser.ParseTrusted</c>.</para>
    /// </summary>
    public string SignatureHeader { get; internal set; } = string.Empty;

    /// <summary>What went wrong, last time. Our exception's type and message — never echoed to a client.</summary>
    public string Error { get; internal set; } = string.Empty;

    public int AttemptCount { get; internal set; }

    public DateTime FirstSeenAt { get; internal set; }

    public DateTime LastAttemptAt { get; internal set; }

    /// <summary>
    /// When a replay applied it. Null means outstanding, and that is the definition the replay endpoint selects on —
    /// so a row that fails again after a successful replay is reopened rather than left looking settled.
    /// </summary>
    public DateTime? ReplayedAt { get; internal set; }

    public static FailedStripeWebhook Capture(
        string? stripeEventId,
        string? eventType,
        string payload,
        string signatureHeader,
        string error,
        DateTime now)
        => new()
        {
            Id = Guid.NewGuid(),
            StripeEventId = Trim(stripeEventId, MaxEventIdLength),
            EventType = Trim(eventType, MaxEventTypeLength),
            Payload = payload,
            SignatureHeader = Trim(signatureHeader, MaxSignatureHeaderLength) ?? string.Empty,
            Error = Trim(error, MaxErrorLength) ?? string.Empty,
            AttemptCount = 1,
            FirstSeenAt = now,
            LastAttemptAt = now
        };

    /// <summary>Stripe redelivered the same event and it failed again.</summary>
    public void RecordAnotherFailure(string error, DateTime now)
    {
        AttemptCount++;
        LastAttemptAt = now;
        Error = Trim(error, MaxErrorLength) ?? string.Empty;
        // Reopened: whatever an earlier replay achieved, this event is outstanding again.
        ReplayedAt = null;
    }

    public void MarkReplayed(DateTime now)
    {
        LastAttemptAt = now;
        ReplayedAt = now;
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
