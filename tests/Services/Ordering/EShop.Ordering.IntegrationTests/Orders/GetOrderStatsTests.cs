using System.Net;
using System.Net.Http.Json;
using EShop.Ordering.Application.Abstractions;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.ValueObjects;
using EShop.Ordering.Infrastructure.Data;
using EShop.Ordering.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Ordering.IntegrationTests.Orders;

/// <summary>
/// Admin panel S8, endpoint #63: <c>GET /api/v1/orders/stats</c>.
///
/// <para>
/// The fixture's tests share a database, so every assertion is made inside a date window this
/// fixture alone writes into — years in the past, one window per test. Asserting "the total is 3"
/// against an unbounded window would depend on which other tests had run.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class GetOrderStatsTests : AuthenticatedIntegrationTestBase
{
    private const string StatsEndpoint = "/api/v1/orders/stats";

    /// <summary>
    /// <c>CreatedAt</c> is set in a second, <c>ExecuteUpdate</c> pass: <c>BaseDbContext</c> overwrites
    /// it on insert whatever the entity held, so seeding it before the first save would silently leave
    /// every order stamped "now" and every bucket assertion testing insertion time.
    /// </summary>
    private async Task SeedAsync(decimal total, OrderStatus status, DateTime createdAt)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();

        var order = Order.Create(
            $"stats-{Guid.NewGuid():N}",
            new Address("1 Stats St", "Sortville", "CA", "90210", "US"),
            [new OrderItem(Guid.NewGuid(), "Counted Widget", total, 1)]);

        switch (status)
        {
            case OrderStatus.Paid:
                order.MarkAsPaid($"pi_{Guid.NewGuid():N}", order.TotalPrice);
                break;
            case OrderStatus.Shipped:
                order.MarkAsPaid($"pi_{Guid.NewGuid():N}", order.TotalPrice);
                order.Ship();
                break;
            case OrderStatus.Delivered:
                order.MarkAsPaid($"pi_{Guid.NewGuid():N}", order.TotalPrice);
                order.Ship();
                order.Deliver();
                break;
            case OrderStatus.Cancelled:
                order.Cancel("seeded");
                break;
            case OrderStatus.Refunded:
                order.MarkAsPaid($"pi_{Guid.NewGuid():N}", order.TotalPrice);
                order.Refund();
                break;
        }

        order.ClearDomainEvents();
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        await db.Orders.Where(o => o.Id == order.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.CreatedAt, createdAt));
    }

    private async Task<OrderStatsResponse> StatsAsync(DateTime from, DateTime to, string? groupBy = null)
    {
        var url = $"{StatsEndpoint}?from={from:O}&to={to:O}"
            + (groupBy is null ? "" : $"&groupBy={groupBy}");
        var response = await Client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<OrderStatsResponse>())!;
    }

    private static DateTime Utc(int year, int month, int day)
        => new(year, month, day, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task Totals_CountEveryOrderInTheWindow_AndOnlyThose()
    {
        await SeedAsync(100m, OrderStatus.Paid, Utc(2030, 3, 10));
        await SeedAsync(50m, OrderStatus.Pending, Utc(2030, 3, 11));
        await SeedAsync(999m, OrderStatus.Paid, Utc(2031, 3, 10)); // outside

        var stats = await StatsAsync(Utc(2030, 1, 1), Utc(2030, 12, 31));

        stats.TotalOrders.Should().Be(2);
        stats.GrossValue.Should().Be(150m);
    }

    /// <summary>
    /// The number a dashboard shows as income. A refund is money returned and a cancellation money
    /// never taken; folding either into revenue is the mistake this split exists to prevent.
    /// </summary>
    [Test]
    public async Task PaidRevenue_CountsPaidShippedAndDelivered_AndNothingElse()
    {
        var from = Utc(2032, 1, 1);
        var to = Utc(2032, 12, 31);
        await SeedAsync(10m, OrderStatus.Pending, Utc(2032, 2, 1));
        await SeedAsync(20m, OrderStatus.Paid, Utc(2032, 2, 2));
        await SeedAsync(30m, OrderStatus.Shipped, Utc(2032, 2, 3));
        await SeedAsync(40m, OrderStatus.Delivered, Utc(2032, 2, 4));
        await SeedAsync(50m, OrderStatus.Cancelled, Utc(2032, 2, 5));
        await SeedAsync(60m, OrderStatus.Refunded, Utc(2032, 2, 6));

        var stats = await StatsAsync(from, to);

        stats.PaidRevenue.Should().Be(90m, "20 + 30 + 40");
        stats.RefundedValue.Should().Be(60m);
        stats.CancelledValue.Should().Be(50m);
        stats.GrossValue.Should().Be(210m, "gross is every order, revenue is not");
    }

    /// <summary>A client charting six bars should not have to know which keys the server omitted.</summary>
    [Test]
    public async Task ByStatus_ListsEveryStatus_IncludingTheEmptyOnes()
    {
        await SeedAsync(10m, OrderStatus.Paid, Utc(2033, 4, 1));

        var stats = await StatsAsync(Utc(2033, 1, 1), Utc(2033, 12, 31));

        stats.ByStatus.Select(s => s.Status).Should().BeEquivalentTo(Enum.GetValues<OrderStatus>());
        stats.ByStatus.Single(s => s.Status == OrderStatus.Paid).Count.Should().Be(1);
        stats.ByStatus.Single(s => s.Status == OrderStatus.Refunded).Count.Should().Be(0);
        stats.ByStatus.Single(s => s.Status == OrderStatus.Refunded).Value.Should().Be(0m);
    }

    [Test]
    public async Task DailyBuckets_StartAtMidnight_AndGroupByDay()
    {
        await SeedAsync(10m, OrderStatus.Paid, Utc(2034, 5, 1));
        await SeedAsync(20m, OrderStatus.Pending, Utc(2034, 5, 1));
        await SeedAsync(30m, OrderStatus.Paid, Utc(2034, 5, 3));

        var stats = await StatsAsync(Utc(2034, 1, 1), Utc(2034, 12, 31), "Day");

        stats.GroupBy.Should().Be(OrderStatsGroupBy.Day);
        stats.Buckets.Should().HaveCount(2, "two distinct days, and empty days are absent rather than zero-filled");
        stats.Buckets[0].PeriodStart.Should().Be(new DateTime(2034, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        stats.Buckets[0].OrderCount.Should().Be(2);
        stats.Buckets[0].GrossValue.Should().Be(30m);
        stats.Buckets[0].PaidRevenue.Should().Be(10m, "only the paid one counts as revenue");
        stats.Buckets[1].PeriodStart.Should().Be(new DateTime(2034, 5, 3, 0, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    public async Task MonthlyBuckets_StartOnTheFirst_AndMergeTheDays()
    {
        await SeedAsync(10m, OrderStatus.Paid, Utc(2035, 5, 1));
        await SeedAsync(20m, OrderStatus.Paid, Utc(2035, 5, 28));
        await SeedAsync(30m, OrderStatus.Paid, Utc(2035, 6, 2));

        var stats = await StatsAsync(Utc(2035, 1, 1), Utc(2035, 12, 31), "month");

        stats.Buckets.Should().HaveCount(2);
        stats.Buckets[0].PeriodStart.Should().Be(new DateTime(2035, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        stats.Buckets[0].OrderCount.Should().Be(2);
        stats.Buckets[0].GrossValue.Should().Be(30m);
        stats.Buckets[1].PeriodStart.Should().Be(new DateTime(2035, 6, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Test]
    public async Task YearlyBuckets_StartOnJanuaryFirst()
    {
        await SeedAsync(10m, OrderStatus.Paid, Utc(2036, 2, 10));
        await SeedAsync(20m, OrderStatus.Paid, Utc(2036, 11, 20));

        var stats = await StatsAsync(Utc(2036, 1, 1), Utc(2036, 12, 31), "YEAR");

        stats.Buckets.Should().HaveCount(1);
        stats.Buckets[0].PeriodStart.Should().Be(new DateTime(2036, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        stats.Buckets[0].OrderCount.Should().Be(2);
        stats.Buckets[0].GrossValue.Should().Be(30m);
    }

    /// <summary>
    /// The buckets and the window totals are two separate queries over the same filter. If they can
    /// disagree, one of them is wrong — and the paid-revenue predicate is spelled out in four places,
    /// which is exactly where a divergence would come from.
    /// </summary>
    [Test]
    public async Task TheBucketsSumToTheWindowTotals()
    {
        await SeedAsync(11m, OrderStatus.Pending, Utc(2037, 1, 5));
        await SeedAsync(22m, OrderStatus.Paid, Utc(2037, 2, 6));
        await SeedAsync(33m, OrderStatus.Delivered, Utc(2037, 3, 7));
        await SeedAsync(44m, OrderStatus.Refunded, Utc(2037, 4, 8));

        var stats = await StatsAsync(Utc(2037, 1, 1), Utc(2037, 12, 31), "Month");

        stats.Buckets.Sum(b => b.OrderCount).Should().Be(stats.TotalOrders);
        stats.Buckets.Sum(b => b.GrossValue).Should().Be(stats.GrossValue);
        stats.Buckets.Sum(b => b.PaidRevenue).Should().Be(stats.PaidRevenue);
    }

    [Test]
    public async Task AnEmptyWindow_AnswersZeroes_NotAnError()
    {
        var stats = await StatsAsync(Utc(2040, 1, 1), Utc(2040, 12, 31));

        stats.TotalOrders.Should().Be(0);
        stats.GrossValue.Should().Be(0m);
        stats.Buckets.Should().BeEmpty();
        stats.ByStatus.Should().HaveCount(Enum.GetValues<OrderStatus>().Length);
        stats.ByStatus.Should().OnlyContain(s => s.Count == 0);
    }

    [Test]
    public async Task TheWindowIsEchoedBack_SoAChartCanLabelItself()
    {
        var from = Utc(2041, 1, 1);
        var to = Utc(2041, 12, 31);

        var stats = await StatsAsync(from, to, "Month");

        stats.From.Should().Be(from);
        stats.To.Should().Be(to);
        stats.GroupBy.Should().Be(OrderStatsGroupBy.Month);
    }

    /// <summary>
    /// <c>week</c> is the grouping a caller is most likely to try and the one this API deliberately
    /// does not have. Daily buckets labelled as weeks would be worse than an error.
    /// </summary>
    [Test]
    public async Task AnUnknownGrouping_IsRejected()
    {
        var response = await Client.GetAsync($"{StatsEndpoint}?groupBy=week");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("GroupBy");
    }

    /// <summary>Every bound is optional, so the bare URL must work — that is the dashboard's first call.</summary>
    [Test]
    public async Task WithNoParametersAtAll_ItAnswers()
    {
        var response = await Client.GetAsync(StatsEndpoint);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var stats = await response.Content.ReadFromJsonAsync<OrderStatsResponse>();
        stats!.GroupBy.Should().Be(OrderStatsGroupBy.Day);
        stats.From.Should().BeNull();
    }
}
