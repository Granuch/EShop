using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.QueryServices;
using EShop.Payment.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace EShop.Payment.IntegrationTests.Persistence;

/// <summary>
/// Admin panel S10, on real PostgreSQL.
///
/// <para>
/// <b>Why this fixture exists at all.</b> Payment's HTTP suite runs on EF InMemory, which executes LINQ in memory:
/// every filter, grouping and ordering the admin reads use "works" there whether or not Npgsql can translate it. The
/// three queries S10 added are exactly the kind that can fail only on a real provider — a <c>GROUP BY</c> on
/// <c>CreatedAt</c>'s date parts (Npgsql 10 has no <c>date_trunc</c> in <c>EF.Functions</c> at all), a conditional
/// <c>SUM</c>, and a descending composite sort. So the tests that prove the behaviour live beside the endpoints, and
/// these prove the SQL exists.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class AdminPaymentReadsSqlTests
{
    private static readonly DateTime Jan = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    private string _connectionString = null!;
    private PaymentDbContext _db = null!;
    private PaymentQueryService _service = null!;

    [SetUp]
    public async Task CreateDatabaseAsync()
    {
        _connectionString = await PostgresTestServer.CreateDatabaseAsync();
        _db = NewContext();
        // The same context: every read below is AsNoTracking, so it comes from the database rather than from whatever
        // the seeding left in the change tracker.
        _service = new PaymentQueryService(_db);
    }

    [TearDown]
    public async Task ReleaseDatabaseAsync()
    {
        await _db.DisposeAsync();
        PostgresTestServer.ReleaseDatabase(_connectionString);
    }

    private PaymentDbContext NewContext() => new(new DbContextOptionsBuilder<PaymentDbContext>()
        .UseNpgsql(_connectionString)
        .Options);

    private static PaymentTransaction A(
        PaymentStatus status,
        decimal amount,
        DateTime createdAt,
        string userId = "customer-1",
        PaymentMethodType method = PaymentMethodType.Stripe)
        => new()
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = userId,
            Amount = amount,
            Currency = "USD",
            PaymentMethod = method,
            PaymentIntentId = $"pi_{Guid.NewGuid():N}",
            Status = status,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };

    /// <summary>
    /// Seeds, then forces <c>CreatedAt</c> with <c>ExecuteUpdate</c>. <c>BaseDbContext.SetAuditFields</c> overwrites
    /// it with the save time on insert, so a date-bucket test that seeds it directly is silently testing insertion
    /// order.
    /// </summary>
    private async Task SeedAsync(params PaymentTransaction[] payments)
    {
        // Read before the save: SetAuditFields mutates the tracked instances, so payment.CreatedAt afterwards is the
        // save time, not the date the test asked for.
        var stamps = payments.Select(p => (p.Id, p.CreatedAt)).ToList();

        _db.PaymentTransactions.AddRange(payments);
        await _db.SaveChangesAsync();

        foreach (var (id, stamp) in stamps)
        {
            await _db.PaymentTransactions
                .Where(p => p.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.CreatedAt, stamp));
        }
    }

    [Test]
    public async Task TheList_RunsOnPostgres_NewestFirst_UnderEveryFilter()
    {
        var newest = A(PaymentStatus.Success, 100m, Jan, "customer-1", PaymentMethodType.Mock);
        var older = A(PaymentStatus.Success, 40m, Jan.AddDays(-1), "customer-1", PaymentMethodType.Mock);
        await SeedAsync(newest, older, A(PaymentStatus.Failed, 10m, Jan, "customer-2"));

        var filter = new PaymentListFilter(
            Statuses: [PaymentStatus.Success],
            UserId: "customer-1",
            OrderId: null,
            PaymentMethod: PaymentMethodType.Mock,
            Currency: "USD",
            From: Jan.AddDays(-2),
            To: Jan,
            MinAmount: 1m,
            MaxAmount: 1000m);

        var (items, totalCount) = await _service.GetPageAsync(filter, 1, 10);

        Assert.Multiple(() =>
        {
            Assert.That(items.Select(p => p.Id), Is.EqualTo(new[] { newest.Id, older.Id }));
            Assert.That(totalCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task TheOrderIdFilter_RunsOnPostgres()
    {
        var target = A(PaymentStatus.Success, 100m, Jan);
        await SeedAsync(target, A(PaymentStatus.Success, 100m, Jan));

        var (items, _) = await _service.GetPageAsync(new PaymentListFilter(OrderId: target.OrderId), 1, 10);

        Assert.That(items.Select(p => p.Id), Is.EqualTo(new[] { target.Id }));
    }

    [Test]
    public async Task TheExportCountAndList_RunOnPostgres_AndRespectTheCap()
    {
        await SeedAsync(
            A(PaymentStatus.Success, 1m, Jan),
            A(PaymentStatus.Success, 2m, Jan.AddMinutes(-1)),
            A(PaymentStatus.Success, 3m, Jan.AddMinutes(-2)));

        var count = await _service.CountAsync(new PaymentListFilter());
        var capped = await _service.ListAsync(new PaymentListFilter(), 2);

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.EqualTo(3));
            Assert.That(capped, Has.Count.EqualTo(2));
        });
    }

    /// <summary>
    /// The one query in this stage that cannot be trusted to InMemory: three <c>GroupBy</c> shapes over
    /// <c>CreatedAt</c>'s integer date parts, each with two conditional <c>SUM</c>s.
    /// </summary>
    [TestCase(PaymentStatsGroupBy.Day, 3)]
    [TestCase(PaymentStatsGroupBy.Month, 2)]
    [TestCase(PaymentStatsGroupBy.Year, 1)]
    public async Task TheStats_GroupOnPostgres_AtEveryBucketSize(PaymentStatsGroupBy groupBy, int expectedBuckets)
    {
        await SeedAsync(
            A(PaymentStatus.Success, 100m, Jan),
            A(PaymentStatus.Refunded, 30m, Jan.AddDays(1)),
            A(PaymentStatus.Failed, 20m, Jan.AddMonths(1)));

        var stats = await _service.GetStatsAsync(new PaymentListFilter(Currency: "USD"), groupBy);

        Assert.Multiple(() =>
        {
            Assert.That(stats.Buckets, Has.Count.EqualTo(expectedBuckets));
            Assert.That(stats.TotalPayments, Is.EqualTo(3));
            Assert.That(stats.GrossAmount, Is.EqualTo(150m));
            Assert.That(stats.CapturedRevenue, Is.EqualTo(100m));
            Assert.That(stats.RefundedAmount, Is.EqualTo(30m));
            Assert.That(stats.FailedAmount, Is.EqualTo(20m));
            Assert.That(stats.Buckets.Sum(b => b.GrossAmount), Is.EqualTo(stats.GrossAmount));
            Assert.That(stats.Buckets.Sum(b => b.CapturedRevenue), Is.EqualTo(stats.CapturedRevenue));
            Assert.That(stats.ByStatus, Has.Count.EqualTo(Enum.GetValues<PaymentStatus>().Length));
        });
    }

    /// <summary>
    /// The bucket start is assembled in memory from the integer parts the database grouped on; this pins that it
    /// comes back as a UTC instant rather than whatever kind Npgsql last handled.
    /// </summary>
    [Test]
    public async Task ABucketStart_IsAUtcInstant()
    {
        await SeedAsync(A(PaymentStatus.Success, 100m, Jan));

        var stats = await _service.GetStatsAsync(new PaymentListFilter(Currency: "USD"), PaymentStatsGroupBy.Day);

        Assert.Multiple(() =>
        {
            Assert.That(stats.Buckets[0].PeriodStart, Is.EqualTo(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(stats.Buckets[0].PeriodStart.Kind, Is.EqualTo(DateTimeKind.Utc));
        });
    }

    /// <summary>
    /// The list's <c>ORDER BY</c> and the indexes <c>AdminPaymentListIndexes</c> creates have to agree, or the
    /// migration is decoration. Checked against the SQL and the catalogue rather than against a query plan, which over
    /// three rows would only tell us what the planner's cost estimates say.
    /// </summary>
    [Test]
    public async Task TheListOrdersByTheColumnsTheIndexesCover()
    {
        var sql = _db.PaymentTransactions
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .ToQueryString();

        var indexes = await _db.Database
            .SqlQueryRaw<string>("""
                SELECT indexdef AS "Value" FROM pg_indexes WHERE tablename = 'PaymentTransactions'
                """)
            .ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(sql, Does.Contain("\"CreatedAt\" DESC"));
            Assert.That(sql, Does.Contain("\"Id\" DESC"));
            Assert.That(indexes.Any(i => i.Contains("(\"CreatedAt\" DESC, \"Id\" DESC)")), Is.True,
                "IX_PaymentTransactions_CreatedAt_Id must descend on both columns: " + string.Join(" | ", indexes));
            Assert.That(indexes.Any(i => i.Contains("(\"Status\", \"CreatedAt\" DESC, \"Id\" DESC)")), Is.True,
                "IX_PaymentTransactions_Status_CreatedAt_Id must descend on the trailing columns: "
                + string.Join(" | ", indexes));
        });
    }
}
