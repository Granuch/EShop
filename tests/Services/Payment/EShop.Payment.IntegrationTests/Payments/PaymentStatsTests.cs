using System.Net;
using System.Net.Http.Json;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Payment.IntegrationTests.Payments;

/// <summary>
/// Admin panel S10 (endpoint #68). <c>GET /api/v1/payments/stats</c>. Each test gets its own host and its own
/// database, so a window here is the whole dataset.
/// </summary>
[TestFixture]
[Category("Integration")]
public class PaymentStatsTests : AuthenticatedIntegrationTestBase
{
    protected override string TestUserRole => "Admin";
    protected override string TestUserId => "admin-1";

    private static readonly DateTime Jan = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    private async Task SeedAsync(
        PaymentStatus status,
        decimal amount,
        DateTime createdAt,
        string currency = "USD")
    {
        var payment = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "customer-1",
            Amount = amount,
            Currency = currency,
            PaymentMethod = PaymentMethodType.Stripe,
            PaymentIntentId = $"pi_{Guid.NewGuid():N}",
            Status = status,
            CreatedAt = createdAt,
            UpdatedAt = createdAt
        };
        await Factory.SeedAsync(payment);

        // BaseDbContext overwrites CreatedAt on insert; set it in a second save, which stamps only UpdatedAt.
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var stored = await db.PaymentTransactions.SingleAsync(p => p.Id == payment.Id);
        stored.CreatedAt = createdAt;
        await db.SaveChangesAsync();
    }

    private async Task<Stats> StatsAsync(string query = "")
    {
        var response = await Client.GetAsync($"/api/v1/payments/stats{query}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Stats>())!;
    }

    private static decimal Of(Stats stats, PaymentStatus status)
        => stats.ByStatus.Single(s => s.Status == status.ToString().ToUpperInvariant()).Amount;

    // ---- Totals ----

    /// <summary>
    /// Captured revenue is successful payments alone. Folding a refund into revenue is the mistake the split exists
    /// to prevent, and the one an operator is least able to spot on a chart.
    /// </summary>
    [Test]
    public async Task CapturedRevenue_CountsSuccessesAndNothingElse()
    {
        await SeedAsync(PaymentStatus.Success, 100m, Jan);
        await SeedAsync(PaymentStatus.Success, 50m, Jan);
        await SeedAsync(PaymentStatus.Refunded, 30m, Jan);
        await SeedAsync(PaymentStatus.Failed, 20m, Jan);
        await SeedAsync(PaymentStatus.Cancelled, 7m, Jan);
        await SeedAsync(PaymentStatus.Pending, 11m, Jan);
        await SeedAsync(PaymentStatus.Processing, 13m, Jan);

        var stats = await StatsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(stats.TotalPayments, Is.EqualTo(7));
            Assert.That(stats.GrossAmount, Is.EqualTo(231m), "every payment, whatever its status");
            Assert.That(stats.CapturedRevenue, Is.EqualTo(150m));
            Assert.That(stats.RefundedAmount, Is.EqualTo(30m));
            Assert.That(stats.FailedAmount, Is.EqualTo(20m));
        });
    }

    /// <summary>
    /// A client charting six bars should not have to know which keys the server chose to omit — and the window totals
    /// read the Refunded and Failed entries with <c>Single(...)</c>, so an incomplete list would be a 500, not a gap.
    /// </summary>
    [Test]
    public async Task ByStatus_CarriesEveryStatus_IncludingTheEmptyOnes()
    {
        await SeedAsync(PaymentStatus.Success, 100m, Jan);

        var stats = await StatsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(stats.ByStatus.Select(s => s.Status),
                Is.EquivalentTo(Enum.GetValues<PaymentStatus>().Select(s => s.ToString().ToUpperInvariant())));
            Assert.That(Of(stats, PaymentStatus.Cancelled), Is.Zero);
            Assert.That(stats.ByStatus.Single(s => s.Status == "CANCELLED").Count, Is.Zero);
        });
    }

    /// <summary>
    /// The status reaches the client as the same upper-case string <c>PaymentDto.Status</c> uses. Serializing the
    /// enum instead would put a number here, so one payment endpoint would report "SUCCESS" and its dashboard 2.
    /// </summary>
    [Test]
    public async Task AStatus_IsTheSameStringThePaymentItselfReports()
    {
        await SeedAsync(PaymentStatus.Success, 100m, Jan);

        var stats = await StatsAsync();

        Assert.That(stats.ByStatus.Select(s => s.Status), Does.Contain("SUCCESS"));
    }

    // ---- The window ----

    [Test]
    public async Task TheWindow_IsInclusiveAtBothEnds_AndIsEchoedBack()
    {
        await SeedAsync(PaymentStatus.Success, 100m, Jan);
        await SeedAsync(PaymentStatus.Success, 50m, Jan.AddDays(-1));
        await SeedAsync(PaymentStatus.Success, 999m, Jan.AddDays(-10));

        var stats = await StatsAsync($"?from={Jan.AddDays(-1):O}&to={Jan:O}");

        Assert.Multiple(() =>
        {
            Assert.That(stats.TotalPayments, Is.EqualTo(2));
            Assert.That(stats.CapturedRevenue, Is.EqualTo(150m));
            Assert.That(stats.From, Is.EqualTo(Jan.AddDays(-1)));
            Assert.That(stats.To, Is.EqualTo(Jan));
        });
    }

    [Test]
    public async Task ADateWithNoTimeZone_IsAcceptedAndReadAsUtc()
    {
        await SeedAsync(PaymentStatus.Success, 100m, Jan);
        await SeedAsync(PaymentStatus.Success, 999m, Jan.AddDays(-10));

        var stats = await StatsAsync("?from=2026-01-15");

        Assert.That(stats.CapturedRevenue, Is.EqualTo(100m));
    }

    /// <summary>
    /// A sum over a money column whose currency column is unconstrained is a number with no unit. Payment writes USD
    /// only today, so this changes nothing now — and the day a second currency appears, the dashboard reports one
    /// instead of silently adding them together.
    /// </summary>
    [Test]
    public async Task TheTotals_AreForOneCurrency_DefaultingToUsd()
    {
        await SeedAsync(PaymentStatus.Success, 100m, Jan);
        await SeedAsync(PaymentStatus.Success, 7m, Jan, currency: "EUR");

        var defaulted = await StatsAsync();
        var euros = await StatsAsync("?currency=eur");

        Assert.Multiple(() =>
        {
            Assert.That(defaulted.Currency, Is.EqualTo("USD"));
            Assert.That(defaulted.CapturedRevenue, Is.EqualTo(100m), "the EUR payment must not be added to the USD total");
            Assert.That(euros.Currency, Is.EqualTo("EUR"));
            Assert.That(euros.CapturedRevenue, Is.EqualTo(7m));
        });
    }

    // ---- Buckets ----

    [Test]
    public async Task DailyBuckets_StartAtMidnight_AndAreOldestFirst()
    {
        await SeedAsync(PaymentStatus.Success, 10m, Jan);
        await SeedAsync(PaymentStatus.Success, 20m, Jan.AddHours(5));
        await SeedAsync(PaymentStatus.Success, 40m, Jan.AddDays(1));

        var stats = await StatsAsync("?groupBy=Day");

        Assert.Multiple(() =>
        {
            Assert.That(stats.GroupBy, Is.EqualTo("Day"));
            Assert.That(stats.Buckets, Has.Count.EqualTo(2));
            Assert.That(stats.Buckets[0].PeriodStart, Is.EqualTo(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(stats.Buckets[0].PaymentCount, Is.EqualTo(2));
            Assert.That(stats.Buckets[0].CapturedRevenue, Is.EqualTo(30m));
            Assert.That(stats.Buckets[1].PeriodStart, Is.EqualTo(new DateTime(2026, 1, 16, 0, 0, 0, DateTimeKind.Utc)));
        });
    }

    [Test]
    public async Task MonthlyBuckets_StartOnTheFirst_AndMergeTheDays()
    {
        await SeedAsync(PaymentStatus.Success, 10m, Jan);
        await SeedAsync(PaymentStatus.Success, 20m, Jan.AddDays(10));
        await SeedAsync(PaymentStatus.Success, 40m, Jan.AddMonths(1));

        var stats = await StatsAsync("?groupBy=Month");

        Assert.Multiple(() =>
        {
            Assert.That(stats.Buckets, Has.Count.EqualTo(2));
            Assert.That(stats.Buckets[0].PeriodStart, Is.EqualTo(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(stats.Buckets[0].PaymentCount, Is.EqualTo(2));
            Assert.That(stats.Buckets[1].PeriodStart, Is.EqualTo(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)));
        });
    }

    [Test]
    public async Task YearlyBuckets_StartOnJanuaryFirst()
    {
        await SeedAsync(PaymentStatus.Success, 10m, Jan);
        await SeedAsync(PaymentStatus.Success, 40m, Jan.AddYears(1));

        var stats = await StatsAsync("?groupBy=year");

        Assert.Multiple(() =>
        {
            Assert.That(stats.Buckets, Has.Count.EqualTo(2));
            Assert.That(stats.Buckets[0].PeriodStart, Is.EqualTo(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(stats.Buckets[1].PeriodStart, Is.EqualTo(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        });
    }

    /// <summary>
    /// The captured-revenue and refunded predicates are written out four times — once for the window totals and once
    /// in each of the three bucket branches — because EF translates an expression tree and cannot translate a call to
    /// a method of ours. This is what catches a drift between the copies, from the other side.
    /// </summary>
    [Test]
    public async Task TheBucketsSumToTheWindowTotals()
    {
        await SeedAsync(PaymentStatus.Success, 100m, Jan);
        await SeedAsync(PaymentStatus.Refunded, 30m, Jan.AddDays(1));
        await SeedAsync(PaymentStatus.Failed, 20m, Jan.AddDays(2));
        await SeedAsync(PaymentStatus.Success, 5m, Jan.AddMonths(1));

        var stats = await StatsAsync("?groupBy=Day");

        Assert.Multiple(() =>
        {
            Assert.That(stats.Buckets.Sum(b => b.PaymentCount), Is.EqualTo(stats.TotalPayments));
            Assert.That(stats.Buckets.Sum(b => b.GrossAmount), Is.EqualTo(stats.GrossAmount));
            Assert.That(stats.Buckets.Sum(b => b.CapturedRevenue), Is.EqualTo(stats.CapturedRevenue));
            Assert.That(stats.Buckets.Sum(b => b.RefundedAmount), Is.EqualTo(stats.RefundedAmount));
        });
    }

    [Test]
    public async Task AnEmptyWindow_IsZeroesAndNoBuckets_NotAnError()
    {
        var stats = await StatsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(stats.TotalPayments, Is.Zero);
            Assert.That(stats.GrossAmount, Is.Zero);
            Assert.That(stats.Buckets, Is.Empty);
            Assert.That(stats.ByStatus, Has.Count.EqualTo(Enum.GetValues<PaymentStatus>().Length));
        });
    }

    // ---- Refusals ----

    [TestCase("?groupBy=week")]
    [TestCase("?groupBy=hour")]
    [TestCase("?currency=DOLLARS")]
    [TestCase("?from=2026-02-01T00:00:00Z&to=2026-01-01T00:00:00Z")]
    public async Task AnUnusableParameter_IsABadRequest(string query)
    {
        var response = await Client.GetAsync($"/api/v1/payments/stats{query}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    /// <summary>"stats" is not a payment id, and the route constraint is what says so.</summary>
    [Test]
    public async Task TheStatsRoute_IsNotReadAsAPaymentId()
    {
        var response = await Client.GetAsync("/api/v1/payments/stats");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private sealed record Stats(
        DateTime? From,
        DateTime? To,
        string Currency,
        string GroupBy,
        int TotalPayments,
        decimal GrossAmount,
        decimal CapturedRevenue,
        decimal RefundedAmount,
        decimal FailedAmount,
        List<StatusRow> ByStatus,
        List<BucketRow> Buckets);

    private sealed record StatusRow(string Status, int Count, decimal Amount);

    private sealed record BucketRow(
        DateTime PeriodStart,
        int PaymentCount,
        decimal GrossAmount,
        decimal CapturedRevenue,
        decimal RefundedAmount);
}
