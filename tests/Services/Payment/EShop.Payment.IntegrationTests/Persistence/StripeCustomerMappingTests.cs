using EShop.Payment.Domain.Entities;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Repositories;
using EShop.Payment.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace EShop.Payment.IntegrationTests.Persistence;

/// <summary>
/// Payment audit Stage 6 (M2), on PostgreSQL. <c>/create-intent</c> stores a user's Stripe customer inside its open
/// transaction. It used to insert through EF and catch the <see cref="DbUpdateException"/> that a concurrent first
/// checkout by the same user raises, then re-read. On Postgres the unique violation aborts the transaction, so the
/// re-read failed with <c>25P02</c> and the checkout failed after all. EF InMemory has no transactions, so only a real
/// database shows it.
/// </summary>
[TestFixture]
public class StripeCustomerMappingTests
{
    private string _connectionString = null!;

    [SetUp]
    public async Task CreateDatabaseAsync() => _connectionString = await PostgresTestServer.CreateDatabaseAsync();

    [TearDown]
    public void ReleaseDatabase() => PostgresTestServer.ReleaseDatabase(_connectionString);

    private PaymentDbContext NewContext() => new(new DbContextOptionsBuilder<PaymentDbContext>()
        .UseNpgsql(_connectionString)
        .Options);

    private static PaymentCustomer Mapping(string userId, string stripeCustomerId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        StripeCustomerId = stripeCustomerId,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    [Test]
    public async Task AUserAlreadyMapped_KeepsTheFirstCustomer_AndTheOpenTransactionCarriesOn()
    {
        await using (var earlier = NewContext())
        {
            await new PaymentRepository(earlier).AddCustomerIfAbsentAsync(Mapping("user-1", "cus_first"));
        }

        await using var db = NewContext();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var stored = await new PaymentRepository(db).AddCustomerIfAbsentAsync(Mapping("user-1", "cus_second"));

        // What the handler does next in the same transaction. After a unique violation Postgres refuses it (25P02).
        var mappings = await db.PaymentCustomers.CountAsync();
        await transaction.CommitAsync();

        Assert.Multiple(() =>
        {
            Assert.That(stored.StripeCustomerId, Is.EqualTo("cus_first"));
            Assert.That(mappings, Is.EqualTo(1));
        });
    }

    /// <summary>Two first checkouts by one user at once: the later waits on the earlier's uncommitted row, then takes it.</summary>
    [Test]
    public async Task TwoFirstCheckoutsAtOnce_BothGetTheEarliersCustomer()
    {
        await using var first = NewContext();
        await using var firstTransaction = await first.Database.BeginTransactionAsync();
        var fromFirst = await new PaymentRepository(first).AddCustomerIfAbsentAsync(Mapping("user-2", "cus_a"));

        await using var second = NewContext();
        await using var secondTransaction = await second.Database.BeginTransactionAsync();
        var fromSecondTask = new PaymentRepository(second).AddCustomerIfAbsentAsync(Mapping("user-2", "cus_b"));

        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.That(fromSecondTask.IsCompleted, Is.False, "the second insert waits on the first's uncommitted row");

        await firstTransaction.CommitAsync();
        var fromSecond = await fromSecondTask.WaitAsync(TimeSpan.FromSeconds(15));
        await secondTransaction.CommitAsync();

        await using var check = NewContext();
        Assert.Multiple(async () =>
        {
            Assert.That(fromFirst.StripeCustomerId, Is.EqualTo("cus_a"));
            Assert.That(fromSecond.StripeCustomerId, Is.EqualTo("cus_a"));
            Assert.That(await check.PaymentCustomers.CountAsync(c => c.UserId == "user-2"), Is.EqualTo(1));
        });
    }
}
