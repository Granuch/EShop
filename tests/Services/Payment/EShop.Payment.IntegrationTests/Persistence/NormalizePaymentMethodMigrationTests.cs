using EShop.Payment.Domain.Entities;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EShop.Payment.IntegrationTests.Persistence;

/// <summary>
/// Payment audit Stage 7 (H5), on a populated table. <c>PaymentMethod</c> became an enum stored as its name. Before
/// Stage 3 it was free text from the request, so a database may hold any string, and reading an unknown one into the
/// enum throws: every lookup of that payment (GET, the webhook, the consumers) would fail. The
/// <c>NormalizePaymentMethod</c> migration maps those values first. An empty database cannot show this, so the test
/// rolls back to the migration before it, inserts the old values, and applies it again.
/// </summary>
[TestFixture]
public class NormalizePaymentMethodMigrationTests
{
    private const string MigrationBefore = "20260911221346_DiscardUndispatchedOutboxBacklog";

    private string _connectionString = null!;

    [SetUp]
    public async Task CreateDatabaseAsync() => _connectionString = await PostgresTestServer.CreateDatabaseAsync();

    [TearDown]
    public void ReleaseDatabase() => PostgresTestServer.ReleaseDatabase(_connectionString);

    private PaymentDbContext NewContext() => new(new DbContextOptionsBuilder<PaymentDbContext>()
        .UseNpgsql(_connectionString)
        .Options);

    [TestCase("Stripe", PaymentMethodType.Stripe)]
    [TestCase("Mock", PaymentMethodType.Mock)]
    [TestCase("None", PaymentMethodType.None)]
    [TestCase("stripe", PaymentMethodType.Stripe, Description = "matched case-insensitively before Stage 7")]
    [TestCase("STRIPE", PaymentMethodType.Stripe)]
    [TestCase("none", PaymentMethodType.None)]
    [TestCase("mock", PaymentMethodType.Mock)]
    [TestCase("card", PaymentMethodType.Mock, Description = "any other value was settled by the simulator")]
    [TestCase("PayPal", PaymentMethodType.Mock)]
    [TestCase("", PaymentMethodType.Mock)]
    public async Task AStoredMethod_ReadsAsTheEnum_AfterTheMigration(string stored, PaymentMethodType expected)
    {
        var orderId = Guid.NewGuid();
        await using (var db = NewContext())
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(MigrationBefore);

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "PaymentTransactions"
                    ("Id", "OrderId", "UserId", "Amount", "Currency", "PaymentMethod", "PaymentIntentId", "Status", "RetryCount", "CreatedAt")
                VALUES ({Guid.NewGuid()}, {orderId}, 'user-1', 10, 'USD', {stored}, 'pi_old', 'Success', 0, now())
                """);

            await migrator.MigrateAsync();
        }

        await using var check = NewContext();
        var payment = await check.PaymentTransactions.AsNoTracking().SingleAsync(p => p.OrderId == orderId);

        Assert.That(payment.PaymentMethod, Is.EqualTo(expected));
    }
}
