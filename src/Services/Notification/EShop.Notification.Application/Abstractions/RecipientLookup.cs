using EShop.Notification.Domain.ValueObjects;

namespace EShop.Notification.Application.Abstractions;

/// <summary>
/// What Identity said about a notification's recipient (Notification audit S3, D3): an address, or the reason the
/// notification can never be delivered. A failure a later attempt may cure is not an outcome but a
/// <see cref="UserContactUnavailableException"/>.
/// </summary>
public sealed record RecipientLookup
{
    private RecipientLookup(RecipientAddress? recipient, string? undeliverableReason)
    {
        Recipient = recipient;
        UndeliverableReason = undeliverableReason;
    }

    public RecipientAddress? Recipient { get; }

    /// <summary>Why the notification can never be delivered; set exactly when <see cref="Recipient"/> is null.</summary>
    public string? UndeliverableReason { get; }

    public static RecipientLookup Found(RecipientAddress recipient)
        => new(recipient ?? throw new ArgumentNullException(nameof(recipient)), null);

    public static RecipientLookup Undeliverable(string reason)
        => string.IsNullOrWhiteSpace(reason)
            ? throw new ArgumentException("A reason is required.", nameof(reason))
            : new RecipientLookup(null, reason);
}

/// <summary>
/// Identity could not tell who the recipient is right now. The consumer records the attempt as failed and rethrows it,
/// so MassTransit retries.
/// </summary>
public sealed class UserContactUnavailableException : Exception
{
    public UserContactUnavailableException(string message, bool isConfigurationError, Exception? innerException = null)
        : base(message, innerException)
    {
        IsConfigurationError = isConfigurationError;
    }

    /// <summary>
    /// Identity refused Notification's credentials (401 or 403). Every notification fails until the API key is fixed, so
    /// it is logged as an error; it is still retried, so that messages survive a short misconfiguration (D3).
    /// </summary>
    public bool IsConfigurationError { get; }
}
