using EShop.Notification.Domain.Entities;
using EShop.Notification.Infrastructure.Data;
using EShop.Notification.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EShop.Notification.IntegrationTests.Persistence;

/// <summary>
/// Admin panel S13 (M13), on a populated table. Every suite's template database is migrated to head, so every other
/// fixture starts with an empty <c>NotificationLogs</c> and cannot show what the migration does to rows that already
/// exist — which, for a 90-day journal, is all of them on the day it deploys. This rolls back to the migration before,
/// writes a row in the old shape, and applies <c>NotificationLogPayload</c> both ways.
/// </summary>
[TestFixture]
[Category("Integration")]
public class NotificationLogPayloadMigrationTests
{
    private const string MigrationBefore = "20260914172214_NotificationDeliveryAttempts";

    private string _connectionString = null!;

    [SetUp]
    public async Task CreateDatabaseAsync() => _connectionString = await PostgresTestServer.CreateDatabaseAsync();

    [TearDown]
    public void ReleaseDatabase() => PostgresTestServer.ReleaseDatabase(_connectionString);

    private NotificationDbContext NewContext() => new(new DbContextOptionsBuilder<NotificationDbContext>()
        .UseNpgsql(_connectionString)
        .Options);

    private async Task<Guid> AFailedRowFromBeforeTheMigrationAsync(IMigrator migrator, NotificationDbContext db)
    {
        var id = Guid.NewGuid();
        await migrator.MigrateAsync(MigrationBefore);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "NotificationLogs"
                ("Id", "EventId", "EventType", "UserId", "TemplateName", "Subject", "Status", "RetryCount", "LastError", "CreatedAt")
            VALUES ({id}, {Guid.NewGuid()}, 'PaymentRefundedEvent', 'user-1', 'payment-refunded', 'written before the payload column',
                    {(int)NotificationStatus.Failed}, 1, 'Email provider connectivity error.', now())
            """);

        return id;
    }

    /// <summary>
    /// A row that predates the column keeps no event: nothing can be invented for it. It reads as "not resendable",
    /// which the resend endpoint reports as a 409 naming why — rather than failing to read, or resending a guess.
    /// </summary>
    [Test]
    public async Task AnExistingRow_SurvivesTheUpgrade_AndReadsAsNotResendable()
    {
        await using var db = NewContext();
        var migrator = db.GetService<IMigrator>();
        var id = await AFailedRowFromBeforeTheMigrationAsync(migrator, db);

        await migrator.MigrateAsync();

        await using var check = NewContext();
        var row = await check.NotificationLogs.AsNoTracking().SingleAsync(l => l.Id == id);
        Assert.Multiple(() =>
        {
            Assert.That(row.Status, Is.EqualTo(NotificationStatus.Failed));
            Assert.That(row.Payload, Is.Null);
            Assert.That(row.ResendBlockerAt(DateTime.UtcNow), Is.EqualTo(NotificationResendBlocker.NoPayload));
        });
    }

    /// <summary>The rollback drops the column from a table holding rows, including one that has a payload, and keeps the rows.</summary>
    [Test]
    public async Task TheRollback_DropsTheColumn_AndKeepsEveryRow()
    {
        await using var db = NewContext();
        var migrator = db.GetService<IMigrator>();
        var old = await AFailedRowFromBeforeTheMigrationAsync(migrator, db);
        await migrator.MigrateAsync();

        var withPayload = NotificationLog.CreatePending(
            Guid.NewGuid(), "PaymentRefundedEvent", null, "user-1", "payment-refunded", "s", "{\"OrderId\":\"x\"}");
        await using (var write = NewContext())
        {
            write.NotificationLogs.Add(withPayload);
            await write.SaveChangesAsync();
        }

        await migrator.MigrateAsync(MigrationBefore);

        var ids = await db.Database
            .SqlQueryRaw<Guid>("""SELECT "Id" AS "Value" FROM "NotificationLogs" """)
            .ToListAsync();
        var payloadColumns = await db.Database
            .SqlQueryRaw<int>("""
                SELECT count(*)::int AS "Value" FROM information_schema.columns
                WHERE table_name = 'NotificationLogs' AND column_name = 'Payload'
                """)
            .SingleAsync();

        Assert.Multiple(() =>
        {
            Assert.That(ids, Is.EquivalentTo(new[] { old, withPayload.Id }));
            Assert.That(payloadColumns, Is.Zero);
        });
    }
}
