using EShop.Notification.Application.Abstractions;
using EShop.Notification.Domain.Entities;
using EShop.Notification.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace EShop.Notification.Infrastructure.Repositories;

/// <summary>
/// Each call is its own commit: no transaction spans a delivery (Notification audit S2, D1, D2), so a failure recorded
/// here stays recorded whatever happens afterwards.
/// </summary>
public sealed class NotificationLogRepository : INotificationLogRepository
{
    private readonly NotificationDbContext _dbContext;

    public NotificationLogRepository(NotificationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<NotificationLog?> FindByEventIdAsync(Guid eventId, CancellationToken ct = default)
        => _dbContext.NotificationLogs.FirstOrDefaultAsync(x => x.EventId == eventId, ct);

    public async Task<bool> TryAddAsync(NotificationLog log, CancellationToken ct = default)
    {
        _dbContext.NotificationLogs.Add(log);

        try
        {
            await _dbContext.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another delivery of the same event inserted first. With no transaction open, the failed INSERT leaves the
            // connection usable, so the caller can read the winner's row.
            _dbContext.Entry(log).State = EntityState.Detached;
            return false;
        }
    }

    public async Task SaveAsync(NotificationLog log, CancellationToken ct = default)
    {
        if (_dbContext.Entry(log).State == EntityState.Detached)
        {
            throw new InvalidOperationException($"NotificationLog {log.Id} is not tracked by this repository.");
        }

        await _dbContext.SaveChangesAsync(ct);
    }
}
