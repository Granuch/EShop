using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Entities;
using EShop.Notification.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EShop.Notification.Infrastructure.Repositories;

public sealed class NotificationLogRepository : INotificationLogRepository
{
    private readonly NotificationDbContext _dbContext;

    public NotificationLogRepository(NotificationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(NotificationLog log, CancellationToken ct = default)
    {
        await _dbContext.NotificationLogs.AddAsync(log, ct);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<NotificationLog?> FindByEventIdAsync(Guid eventId, CancellationToken ct = default)
    {
        return await _dbContext.NotificationLogs
            .FirstOrDefaultAsync(x => x.EventId == eventId, ct);
    }

    public async Task UpdateAsync(NotificationLog log, CancellationToken ct = default)
    {
        _dbContext.NotificationLogs.Update(log);
        await _dbContext.SaveChangesAsync(ct);
    }
}
