using EShop.Notification.Domain.Entities;
using EShop.Notification.Infrastructure.BackgroundServices;
using EShop.Notification.Infrastructure.Configuration;
using EShop.Notification.Infrastructure.Data;
using EShop.Notification.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace EShop.Notification.IntegrationTests.Persistence;

/// <summary>
/// Notification audit S5 (M8, D8), on PostgreSQL: the bulk delete is relational-only. Rows are aged with
/// <c>ExecuteUpdate</c>, because <c>BaseDbContext</c> overwrites <c>UpdatedAt</c> on every <c>SaveChanges</c>.
/// </summary>
[TestFixture]
public class NotificationLogRetentionTests
{
    private string _connectionString = null!;

    [SetUp]
    public async Task CreateDatabaseAsync() => _connectionString = await PostgresTestServer.CreateDatabaseAsync();

    [TearDown]
    public void ReleaseDatabase() => PostgresTestServer.ReleaseDatabase(_connectionString);

    [Test]
    public async Task RowsThatEndedMoreThanNinetyDaysAgo_AreDeleted_AndNewerOnesKept()
    {
        var now = DateTime.UtcNow;
        var expired = await SeedAsync(now.AddDays(-91));
        var recent = await SeedAsync(now.AddDays(-89));
        var fresh = await SeedAsync(now);

        var deleted = await DeleteExpiredAsync(new NotificationLogRetentionSettings(), now);

        Assert.Multiple(async () =>
        {
            Assert.That(deleted, Is.EqualTo(1));
            Assert.That(await RemainingAsync(), Is.EquivalentTo(new[] { recent, fresh }));
        });
        Assert.That(await RemainingAsync(), Does.Not.Contain(expired));
    }

    [Test]
    public async Task ExpiredRows_AreDeletedInBatches_UntilNoneRemain()
    {
        var now = DateTime.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            await SeedAsync(now.AddDays(-100));
        }

        var fresh = await SeedAsync(now);

        var deleted = await DeleteExpiredAsync(new NotificationLogRetentionSettings { BatchSize = 2 }, now);

        Assert.Multiple(async () =>
        {
            Assert.That(deleted, Is.EqualTo(5));
            Assert.That(await RemainingAsync(), Is.EqualTo(new[] { fresh }));
        });
    }

    private NotificationDbContext NewContext() => new(new DbContextOptionsBuilder<NotificationDbContext>()
        .UseNpgsql(_connectionString)
        .Options);

    private async Task<int> DeleteExpiredAsync(NotificationLogRetentionSettings settings, DateTime now)
    {
        await using var db = NewContext();
        return await NotificationLogRetentionService.DeleteExpiredAsync(db, settings, now, CancellationToken.None);
    }

    private async Task<Guid> SeedAsync(DateTime lastUpdate)
    {
        await using var db = NewContext();
        var log = NotificationLog.CreatePending(Guid.NewGuid(), "Event", null, "user-1", "template", "subject");
        log.BeginAttempt(DateTime.UtcNow);
        log.RecordRecipient("user@test.com");
        log.MarkSent(providerMessageId: null);
        db.NotificationLogs.Add(log);
        await db.SaveChangesAsync();

        await db.NotificationLogs
            .Where(l => l.Id == log.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.UpdatedAt, (DateTime?)lastUpdate));

        return log.Id;
    }

    private async Task<List<Guid>> RemainingAsync()
    {
        await using var db = NewContext();
        return await db.NotificationLogs.AsNoTracking().Select(l => l.Id).ToListAsync();
    }
}
