using EShop.Notification.Domain.ValueObjects;

namespace EShop.Notification.Application.Abstractions;

public interface IUserContactResolver
{
    Task<RecipientAddress?> ResolveAsync(string userId, CancellationToken ct = default);
}
