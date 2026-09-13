using EShop.Payment.Domain.Entities;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace EShop.Payment.IntegrationTests.Persistence;

/// <summary>
/// Payment audit Stage 10 (M11), on Postgres. A Stripe intent belongs to one payment. The webhook finds its payment by
/// intent id and used to take whichever row came first, because the index on <c>PaymentIntentId</c> was not unique. It
/// is now unique among payments that have an intent. Payments with none yet hold <c>''</c>, and are left out.
/// </summary>
[TestFixture]
public class PaymentIntentUniquenessTests
{
    private const string MigrationBefore = "20260913124829_NormalizePaymentMethod";

    private string _connectionString = null!;

    [SetUp]
    public async Task CreateDatabaseAsync() => _connectionString = await PostgresTestServer.CreateDatabaseAsync();

    [TearDown]
    public void ReleaseDatabase() => PostgresTestServer.ReleaseDatabase(_connectionString);

    private PaymentDbContext NewContext() => new(new DbContextOptionsBuilder<PaymentDbContext>()
        .UseNpgsql(_connectionString)
        .Options);

    private static PaymentTransaction StripePayment(string intentId) => new()
    {
        Id = Guid.NewGuid(),
        OrderId = Guid.NewGuid(),
        UserId = "user-1",
        Amount = 10m,
        Currency = "USD",
        PaymentMethod = PaymentMethodType.Stripe,
        PaymentIntentId = intentId,
        Status = intentId.Length == 0 ? PaymentStatus.Pending : PaymentStatus.Processing,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    [Test]
    public async Task TwoPayments_CannotShareAStripeIntent()
    {
        await using (var first = NewContext())
        {
            first.PaymentTransactions.Add(StripePayment("pi_shared"));
            await first.SaveChangesAsync();
        }

        await using var second = NewContext();
        second.PaymentTransactions.Add(StripePayment("pi_shared"));

        var ex = Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        Assert.That(ex!.InnerException, Is.InstanceOf<PostgresException>()
            .With.Property(nameof(PostgresException.SqlState)).EqualTo(PostgresErrorCodes.UniqueViolation));
    }

    [Test]
    public async Task PaymentsWithNoIntentYet_AreNotConstrained()
    {
        await using (var db = NewContext())
        {
            db.PaymentTransactions.AddRange(StripePayment(""), StripePayment(""), StripePayment(""));
            await db.SaveChangesAsync();
        }

        await using var check = NewContext();
        Assert.That(await check.PaymentTransactions.CountAsync(p => p.PaymentIntentId == ""), Is.EqualTo(3));
    }

    /// <summary>
    /// A database where two payments already share an intent cannot take the index. The migration says so in its own
    /// words, and is not recorded as applied, rather than failing on the bare unique violation.
    /// </summary>
    [Test]
    public async Task TheMigration_RefusesADatabaseWhereTwoPaymentsShareAnIntent_AndSaysWhy()
    {
        await using var db = NewContext();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync(MigrationBefore);

        for (var i = 0; i < 2; i++)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "PaymentTransactions"
                    ("Id", "OrderId", "UserId", "Amount", "Currency", "PaymentMethod", "PaymentIntentId", "Status", "CreatedAt")
                VALUES ({Guid.NewGuid()}, {Guid.NewGuid()}, 'user-1', 10, 'USD', 'Stripe', 'pi_dup', 'Processing', now())
                """);
        }

        var ex = Assert.CatchAsync(() => migrator.MigrateAsync());
        var raised = ex as PostgresException ?? ex?.InnerException as PostgresException;

        Assert.That(raised, Is.Not.Null, ex?.ToString());
        Assert.Multiple(() =>
        {
            Assert.That(raised!.SqlState, Is.EqualTo("P0001"));
            Assert.That(raised.MessageText, Does.Contain("1 Stripe intent id(s) are recorded on more than one payment"));
        });

        var applied = await db.Database
            .SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM "__EFMigrationsHistory" WHERE "MigrationId" LIKE '%UniquePaymentIntentIdAndDropRetryCount'""")
            .SingleAsync();
        Assert.That(applied, Is.Zero, "the migration must not be recorded as applied");
    }
}
