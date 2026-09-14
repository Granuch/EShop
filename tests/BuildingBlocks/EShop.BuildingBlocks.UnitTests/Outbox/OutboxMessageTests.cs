using EShop.BuildingBlocks.Domain.Outbox;
using EShop.BuildingBlocks.Messaging;
using EShop.BuildingBlocks.Messaging.Events;

namespace EShop.BuildingBlocks.UnitTests.Outbox;

/// <summary>
/// TEST-06. The outbox is how every integration event in the repo reaches RabbitMQ, and nothing
/// asserted anything about it before Stage 8 — despite Identity running with
/// <c>UseOutbox =&gt; true</c>.
///
/// <para>
/// The redaction tests here are the security-relevant half. An outbox row is a copy of the event
/// payload sitting in the database for a week (<c>OutboxCleanupService.RetentionDays = 7</c>), so
/// any secret in an event is a secret at rest for seven days — that is how a live password-reset
/// token ended up readable in <c>outbox_messages</c> (SEC-05). Redaction cuts the exposure to
/// about one polling interval, and it is safe only because the processor's query filters on
/// <c>ProcessedOnUtc IS NULL</c>, so a payload is never re-read after dispatch.
/// </para>
/// </summary>
[TestFixture]
public class OutboxMessageTests
{
    private const string Payload = """{"resetToken":"a-live-secret-token"}""";

    private static OutboxMessage NewMessage() => OutboxMessage.CreateForIntegration(
        Guid.NewGuid(),
        typeof(PasswordResetRequestedIntegrationEvent),
        Payload,
        DateTime.UtcNow,
        correlationId: "corr-1");

    [Test]
    public void NewMessage_StartsPendingAndUnprocessed()
    {
        var message = NewMessage();

        Assert.That(message.Status, Is.EqualTo(OutboxMessageStatus.Pending));
        Assert.That(message.ProcessedOnUtc, Is.Null);
        Assert.That(message.RetryCount, Is.Zero);
        Assert.That(message.LastError, Is.Null);
        Assert.That(message.Payload, Is.EqualTo(Payload));
    }

    /// <summary>
    /// The default must be "keep the payload". Redaction is opt-in per event type, so a plain
    /// event's payload has to survive processing — several operational workflows read it back.
    /// </summary>
    [Test]
    public void MarkAsProcessed_KeepsThePayloadByDefault()
    {
        var message = NewMessage();

        message.MarkAsProcessed();

        Assert.That(message.Status, Is.EqualTo(OutboxMessageStatus.Processed));
        Assert.That(message.ProcessedOnUtc, Is.Not.Null);
        Assert.That(message.Payload, Is.EqualTo(Payload));
    }

    [Test]
    public void MarkAsProcessed_ClearsTheSecretWhenRedactionIsRequested()
    {
        var message = NewMessage();

        message.MarkAsProcessed(redactPayload: true);

        Assert.That(message.Payload, Is.EqualTo(OutboxMessage.RedactedPayload));
        Assert.That(message.Payload, Does.Not.Contain("a-live-secret-token"));
    }

    /// <summary>
    /// A dead-lettered message is never retried, so a secret in its payload is pure liability —
    /// and it is the row most likely to be read by a human during an incident.
    /// </summary>
    [Test]
    public void MarkAsDeadLettered_ClearsTheSecretWhenRedactionIsRequested()
    {
        var message = NewMessage();

        message.MarkAsDeadLettered("broker refused the message", redactPayload: true);

        Assert.That(message.Status, Is.EqualTo(OutboxMessageStatus.DeadLettered));
        Assert.That(message.Payload, Is.EqualTo(OutboxMessage.RedactedPayload));
        Assert.That(message.LastError, Is.EqualTo("broker refused the message"),
            "the investigation still needs the error, the type and the correlation id — just not the secret");
    }

    /// <summary>
    /// The redacted payload must stay valid JSON: anything that does try to deserialize an
    /// already-terminal row should get an all-defaults instance rather than an exception.
    /// </summary>
    [Test]
    public void RedactedPayload_IsValidJson()
    {
        Assert.That(
            () => System.Text.Json.JsonDocument.Parse(OutboxMessage.RedactedPayload),
            Throws.Nothing);
    }

    /// <summary>
    /// Redaction is keyed off <c>ISensitivePayloadEvent</c>, which the processor tests for by
    /// assignability. If the marker were ever removed from this event the redaction would silently
    /// stop, so the association is worth asserting directly.
    /// </summary>
    [Test]
    public void PasswordResetRequested_IsMarkedAsCarryingASensitivePayload()
    {
        Assert.That(
            typeof(ISensitivePayloadEvent).IsAssignableFrom(typeof(PasswordResetRequestedIntegrationEvent)),
            Is.True);
    }

    [Test]
    public void RecordFailure_IncrementsRetryCountWithoutMarkingTerminal()
    {
        var message = NewMessage();

        message.RecordFailure("transient broker error");

        Assert.That(message.RetryCount, Is.EqualTo(1));
        Assert.That(message.LastError, Is.EqualTo("transient broker error"));
        Assert.That(message.Status, Is.EqualTo(OutboxMessageStatus.Pending), "a retryable failure is not terminal");
        Assert.That(message.ProcessedOnUtc, Is.Null);
    }

    /// <summary>
    /// LastError is stored in a bounded column; an overlong provider message must be truncated
    /// rather than blowing up the write that is trying to record the failure.
    /// </summary>
    [Test]
    public void RecordFailure_TruncatesAnOverlongError()
    {
        var message = NewMessage();

        message.RecordFailure(new string('x', 5000));

        Assert.That(message.LastError, Has.Length.EqualTo(4000));
    }

    [Test]
    public void CanRetry_IsFalseOnceTheMessageIsTerminal()
    {
        var message = NewMessage();
        Assert.That(message.CanRetry(), Is.True);

        message.MarkAsProcessed();

        Assert.That(message.CanRetry(), Is.False, "a processed message must never be redelivered");
    }

    [Test]
    public void CanRetry_IsFalseOnceTheRetryBudgetIsSpent()
    {
        var message = NewMessage();

        for (var i = 0; i < 5; i++)
        {
            message.RecordFailure("still failing");
        }

        Assert.That(message.CanRetry(maxRetries: 5), Is.False);
    }
}
