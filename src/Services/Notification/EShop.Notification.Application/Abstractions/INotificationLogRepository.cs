using EShop.Notification.Domain.Entities;

namespace EShop.Notification.Application.Abstractions;

/// <summary>
/// The delivery log. Every write is its own commit (Notification audit S2): no transaction spans a delivery, so what is
/// recorded stays recorded.
/// </summary>
public interface INotificationLogRepository
{
    Task<NotificationLog?> FindByEventIdAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>Inserts the log; false when a log for the same event already exists (another delivery inserted it).</summary>
    Task<bool> TryAddAsync(NotificationLog log, CancellationToken ct = default);

    /// <summary>
    /// Saves the changes to a log this repository returned or inserted. Throws <c>DbUpdateConcurrencyException</c> when
    /// another delivery changed it since it was read.
    /// </summary>
    Task SaveAsync(NotificationLog log, CancellationToken ct = default);
}
