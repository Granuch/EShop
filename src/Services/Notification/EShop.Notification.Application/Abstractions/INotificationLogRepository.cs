using EShop.Notification.Domain.Entities;

namespace EShop.Notification.Application.Abstractions;

public interface INotificationLogRepository
{
    Task AddAsync(NotificationLog log, CancellationToken ct = default);
    Task<NotificationLog?> FindByEventIdAsync(Guid eventId, CancellationToken ct = default);
    Task UpdateAsync(NotificationLog log, CancellationToken ct = default);
}
