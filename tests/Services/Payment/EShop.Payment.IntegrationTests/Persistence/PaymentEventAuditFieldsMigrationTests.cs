using EShop.Payment.Infrastructure.Data;
using EShop.Payment.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EShop.Payment.IntegrationTests.Persistence;

/// <summary>
/// Admin panel S11, on a populated table. <c>PaymentEventAuditFields</c> adds a <b>NOT NULL</b> <c>CreatedAt</c> to a
/// table that may already hold rows, and EF scaffolded it as <c>defaultValue: DateTime.MinValue</c> — which Npgsql
/// renders as <c>NOT NULL DEFAULT TIMESTAMPTZ '-infinity'</c>. That is wrong twice over, and an empty database shows
/// neither fault, so this rolls back to the migration before it, inserts a row, and applies it again.
/// </summary>
[TestFixture]
[Category("Integration")]
public class PaymentEventAuditFieldsMigrationTests
{
    private const string MigrationBefore = "20260920182412_PaymentDiagnostics";

    private static readonly DateTime Occurred = new(2026, 9, 1, 8, 30, 0, DateTimeKind.Utc);

    private string _connectionString = null!;

    [SetUp]
    public async Task CreateDatabaseAsync() => _connectionString = await PostgresTestServer.CreateDatabaseAsync();

    [TearDown]
    public void ReleaseDatabase() => PostgresTestServer.ReleaseDatabase(_connectionString);

    private PaymentDbContext NewContext() => new(new DbContextOptionsBuilder<PaymentDbContext>()
        .UseNpgsql(_connectionString)
        .Options);

    /// <summary>Rolls back past the migration, writes one timeline row in the old shape, and applies it again.</summary>
    private async Task<Guid> ATimelineRowFromBeforeTheMigrationAsync()
    {
        var eventId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();

        await using var db = NewContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBefore);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "PaymentTransactions"
                ("Id", "OrderId", "UserId", "Amount", "Currency", "PaymentMethod", "PaymentIntentId", "Status", "CreatedAt")
            VALUES ({paymentId}, {Guid.NewGuid()}, 'user-1', 10, 'USD', 'Stripe', 'pi_old', 'Success', now())
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "PaymentEvents"
                ("Id", "PaymentTransactionId", "Kind", "ToStatus", "Detail", "OccurredAt")
            VALUES ({eventId}, {paymentId}, 'Transition', 'Pending', 'written before the audit columns existed', {Occurred})
            """);

        await migrator.MigrateAsync();
        return eventId;
    }

    /// <summary>
    /// <c>-infinity</c> is not a date anyone can act on, and <c>OccurredAt</c> is a truthful approximation: every row
    /// written so far was saved in the same transaction as the transition it records, so the save time and the
    /// transition time are the same instant.
    /// </summary>
    [Test]
    public async Task AnExistingRow_IsBackfilledFromOccurredAt_NotWithASentinel()
    {
        var eventId = await ATimelineRowFromBeforeTheMigrationAsync();

        await using var check = NewContext();
        var row = await check.PaymentEvents.AsNoTracking().SingleAsync(e => e.Id == eventId);

        Assert.Multiple(() =>
        {
            Assert.That(row.CreatedAt, Is.EqualTo(Occurred));
            Assert.That(row.CreatedAt, Is.Not.EqualTo(DateTime.MinValue), "-infinity is what EF scaffolded");
            Assert.That(row.CreatedBy, Is.Null, "nobody can be named for a row written before the column existed");
        });
    }

    /// <summary>
    /// The scaffolded <c>DEFAULT</c> would stay on the column forever, so a raw <c>INSERT</c> that forgot
    /// <c>CreatedAt</c> would silently store <c>-infinity</c> instead of failing. <c>SetAuditFields</c> always
    /// supplies a value, so nothing needs the default.
    /// </summary>
    [Test]
    public async Task TheColumnKeepsNoDefault_SoAnInsertThatForgetsItFailsLoudly()
    {
        var eventId = await ATimelineRowFromBeforeTheMigrationAsync();

        await using var check = NewContext();
        var defaults = await check.Database
            .SqlQueryRaw<string?>("""
                SELECT column_default AS "Value" FROM information_schema.columns
                WHERE table_name = 'PaymentEvents' AND column_name = 'CreatedAt'
                """)
            .ToListAsync();

        Assert.That(defaults, Is.EqualTo(new string?[] { null }));

        Assert.CatchAsync(async () => await check.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "PaymentEvents"
                ("Id", "PaymentTransactionId", "Kind", "ToStatus", "Detail", "OccurredAt")
            SELECT {Guid.NewGuid()}, "PaymentTransactionId", 'Transition', 'Pending', 'no CreatedAt', now()
            FROM "PaymentEvents" WHERE "Id" = {eventId}
            """));
    }
}
