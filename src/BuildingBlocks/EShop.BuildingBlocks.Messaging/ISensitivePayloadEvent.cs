namespace EShop.BuildingBlocks.Messaging;

/// <summary>
/// Marks an event whose serialized payload carries a secret (a reset token, a one-time code)
/// that must not stay readable in the outbox table after the event has been dispatched.
///
/// The outbox processor redacts the stored payload of such a message the moment it is marked
/// processed or dead-lettered, so the secret is at rest only for the time it takes the
/// processor to pick the row up — not for the outbox retention window, which is measured in
/// days and is far longer than the secret's own lifetime.
///
/// Redaction is safe because a processed row is never re-read: the processor's query filters
/// on <c>ProcessedOnUtc IS NULL</c>, so a payload is only ever needed while the message is
/// still pending. A pending message keeps its payload however long delivery takes.
/// </summary>
public interface ISensitivePayloadEvent
{
}
