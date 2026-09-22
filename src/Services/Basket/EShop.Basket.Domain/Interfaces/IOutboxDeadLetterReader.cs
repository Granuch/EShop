namespace EShop.Basket.Domain.Interfaces;

/// <summary>
/// Reads the outbox's dead letters, newest first (Admin panel S14, #81). Each is a checkout Ordering never received;
/// the count and the replay already existed (Basket audit S7), and this is what lets an operator see which ones they
/// are before replaying them.
/// </summary>
public interface IOutboxDeadLetterReader
{
    Task<OutboxDeadLetterPage> ReadDeadLettersAsync(int offset, int limit, CancellationToken cancellationToken = default);
}

/// <summary>
/// One dead letter, as far as it can be read. The envelope's payload — the order, with its shipping address — is
/// deliberately not part of it.
///
/// <para>The failure fields are null for a message dead-lettered before Admin panel S14 began recording them, and
/// every field but <see cref="IsReadable"/> is null for an entry that is not a readable envelope at all.</para>
/// </summary>
public sealed record OutboxDeadLetter(
    bool IsReadable,
    Guid? MessageId,
    string? EventType,
    DateTime? OccurredOnUtc,
    DateTime? DeadLetteredAtUtc,
    int? Attempts,
    string? FailureReason,
    string? ExceptionType,
    string? CorrelationId);

public sealed record OutboxDeadLetterPage(long Total, IReadOnlyList<OutboxDeadLetter> Entries);

/// <summary>
/// Why a message was dead-lettered. Our own words, never the exception's message: a broker exception's text can name
/// hosts and users, and this reaches an HTTP response. The exception's type name is recorded beside it.
/// </summary>
public static class OutboxDeadLetterReasons
{
    /// <summary>Every publish attempt failed.</summary>
    public const string PublishFailed = "PublishFailed";

    /// <summary>The last publish attempt outlived the publish timeout.</summary>
    public const string PublishTimedOut = "PublishTimedOut";

    /// <summary>The envelope's type could not be resolved or its payload read, so it could never be published.</summary>
    public const string Unpublishable = "Unpublishable";
}
