namespace EShop.Notification.Application.Abstractions;

public interface IUserContactResolver
{
    /// <summary>
    /// The recipient's address, or the reason the notification can never be delivered (Notification audit D3). Throws
    /// <see cref="UserContactUnavailableException"/> when Identity cannot answer now, so the message is retried.
    /// </summary>
    Task<RecipientLookup> ResolveAsync(string userId, CancellationToken ct = default);
}
