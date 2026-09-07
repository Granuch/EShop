using System.Text.Json;
using EShop.BuildingBlocks.Domain.Outbox;
using EShop.BuildingBlocks.Messaging;
using EShop.BuildingBlocks.Messaging.Events;

namespace EShop.Identity.UnitTests.BuildingBlocks;

/// <summary>
/// SEC-05. ForgotPassword writes a live password-reset token into the outbox as plaintext JSON,
/// and OutboxCleanupService keeps processed rows for seven days — so a token whose own lifetime
/// is a day sat readable in the database for a week, and anyone with read access to
/// outbox_messages could reset any account that had used "forgot password".
///
/// The fix redacts the payload the moment the message becomes terminal rather than shortening
/// the retention window: the exposure then lasts as long as dispatch takes (about one polling
/// interval) instead of days, and it needs no schema change. It is safe because the processor
/// only ever reads rows with ProcessedOnUtc IS NULL.
///
/// There is no BuildingBlocks test project (see the root CLAUDE.md), so this lives in the
/// Identity suite — Identity owns the only sensitive event today.
/// </summary>
[TestFixture]
public class OutboxSensitivePayloadTests
{
    private static OutboxMessage CreateMessage(string payload) =>
        OutboxMessage.CreateForIntegration(
            Guid.NewGuid(),
            typeof(PasswordResetRequestedIntegrationEvent),
            payload,
            DateTime.UtcNow);

    [Test]
    public void PasswordResetRequested_IsMarkedAsCarryingASensitivePayload()
    {
        // The processor decides whether to redact from this marker alone, so losing it would
        // silently restore the seven-day plaintext window.
        Assert.That(
            typeof(ISensitivePayloadEvent).IsAssignableFrom(typeof(PasswordResetRequestedIntegrationEvent)),
            Is.True);
    }

    [Test]
    public void MarkAsProcessed_WithRedaction_RemovesTheSecretFromThePayload()
    {
        var message = CreateMessage("""{"userId":"user-1","resetToken":"super-secret-token"}""");

        message.MarkAsProcessed(redactPayload: true);

        Assert.That(message.Payload, Does.Not.Contain("super-secret-token"));
        Assert.That(message.Payload, Is.EqualTo(OutboxMessage.RedactedPayload));
        Assert.That(message.Status, Is.EqualTo(OutboxMessageStatus.Processed));
    }

    [Test]
    public void MarkAsDeadLettered_WithRedaction_RemovesTheSecretButKeepsTheDiagnostics()
    {
        var message = CreateMessage("""{"userId":"user-1","resetToken":"super-secret-token"}""");

        message.MarkAsDeadLettered("broker rejected the message", redactPayload: true);

        Assert.That(message.Payload, Does.Not.Contain("super-secret-token"));
        Assert.That(message.Status, Is.EqualTo(OutboxMessageStatus.DeadLettered));
        Assert.That(message.LastError, Is.EqualTo("broker rejected the message"));
        Assert.That(message.Type, Is.EqualTo(typeof(PasswordResetRequestedIntegrationEvent).FullName));
    }

    [Test]
    public void MarkAsProcessed_WithoutRedaction_LeavesThePayloadAlone()
    {
        const string payload = """{"orderId":"order-1"}""";
        var message = CreateMessage(payload);

        message.MarkAsProcessed();

        Assert.That(message.Payload, Is.EqualTo(payload),
            "Redaction is opt-in; ordinary events keep their payload for replay and debugging.");
    }

    [Test]
    public void RedactedPayload_IsValidJson()
    {
        // A processed row is never re-read by the processor, but a redacted payload that could
        // not be parsed would turn any ad-hoc tooling over the outbox table into a crash.
        Assert.DoesNotThrow(() => JsonDocument.Parse(OutboxMessage.RedactedPayload));
    }
}
