using System.Net;
using System.Net.Http.Json;
using EShop.Ordering.Domain.Entities;
using EShop.Ordering.Domain.ValueObjects;
using EShop.Ordering.Infrastructure.Data;
using EShop.Ordering.IntegrationTests.Models;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Ordering.IntegrationTests.Orders;

/// <summary>
/// Admin panel S8, endpoint #56: the filters the admin list gained beyond <c>status</c> and paging.
///
/// <para>
/// Every test scopes itself to its own orders through a unique <c>UserId</c> plus the <c>search</c>
/// filter, because the host and its database are fixture-scoped and a fixture's tests see each
/// other's rows. Asserting "the page contains exactly these" against an unscoped list would depend on
/// the order NUnit happened to run the tests in.
/// </para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class GetOrdersFilterTests : AuthenticatedIntegrationTestBase
{
    private const string OrdersEndpoint = "/api/v1/orders";

    /// <summary>
    /// Seeds an order directly, so its status and <c>CreatedAt</c> can be chosen. <c>CreatedAt</c>
    /// needs a second save: <c>BaseDbContext.SetAuditFields</c> overwrites it on insert whatever the
    /// entity held, so a seeded value assigned before the first save is silently replaced by "now" and
    /// a date-filter test would quietly be testing insertion order.
    /// </summary>
    private async Task<Order> SeedAsync(
        string userId,
        decimal unitPrice,
        int quantity,
        OrderStatus status,
        DateTime? createdAt = null)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderingDbContext>();

        var order = Order.Create(
            userId,
            new Address("1 Filter St", "Sortville", "CA", "90210", "US"),
            [new OrderItem(Guid.NewGuid(), "Filtered Widget", unitPrice, quantity)]);

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

        if (createdAt is { } stamp)
        {
            // ExecuteUpdate bypasses SaveChanges, which is the only way past SetAuditFields.
            await db.Orders.Where(o => o.Id == order.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.CreatedAt, stamp));
        }

        return order;
    }

    private async Task<PagedOrderResponse> QueryAsync(string queryString)
    {
        var response = await Client.GetAsync($"{OrdersEndpoint}?{queryString}&pageSize=100");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PagedOrderResponse>())!;
    }

    /// <summary>A key nothing else in the fixture can match, so each test sees only its own rows.</summary>
    private static string NewScope() => $"s8-{Guid.NewGuid():N}";

    [Test]
    public async Task Statuses_AreAUnion_NotASingleValue()
    {
        var scope = NewScope();
        await SeedAsync($"{scope}-a", 10m, 1, OrderStatus.Pending);
        var paid = await SeedAsync($"{scope}-b", 20m, 1, OrderStatus.Paid);
        var shipped = await SeedAsync($"{scope}-c", 30m, 1, OrderStatus.Shipped);

        var page = await QueryAsync($"search={scope}&statuses=Paid&statuses=Shipped");

        page.Items.Select(o => o.Id).Should().BeEquivalentTo(new[] { paid.Id, shipped.Id });
    }

    /// <summary>The single legacy parameter still works, and folds into the same set.</summary>
    [Test]
    public async Task TheLegacyStatusParameter_UnionsWithStatuses()
    {
        var scope = NewScope();
        var pending = await SeedAsync($"{scope}-a", 10m, 1, OrderStatus.Pending);
        var paid = await SeedAsync($"{scope}-b", 20m, 1, OrderStatus.Paid);
        await SeedAsync($"{scope}-c", 30m, 1, OrderStatus.Shipped);

        var page = await QueryAsync($"search={scope}&status=Pending&statuses=Paid");

        page.Items.Select(o => o.Id).Should().BeEquivalentTo(new[] { pending.Id, paid.Id });
    }

    [Test]
    public async Task OneUnknownStatusAmongSeveral_IsRejected_RatherThanQuietlyDropped()
    {
        var response = await Client.GetAsync($"{OrdersEndpoint}?statuses=Paid&statuses=Payed");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Statuses");
    }

    [Test]
    public async Task Search_MatchesTheUserId_CaseInsensitively()
    {
        var scope = NewScope();
        var mine = await SeedAsync($"{scope}-CUSTOMER", 10m, 1, OrderStatus.Pending);
        await SeedAsync(NewScope(), 10m, 1, OrderStatus.Pending);

        var page = await QueryAsync($"search={scope.ToUpperInvariant()}-customer");

        page.Items.Select(o => o.Id).Should().BeEquivalentTo(new[] { mine.Id });
    }

    [Test]
    public async Task Search_MatchesThePaymentIntentId()
    {
        var scope = NewScope();
        var paid = await SeedAsync($"{scope}-a", 10m, 1, OrderStatus.Paid);
        await SeedAsync($"{scope}-b", 10m, 1, OrderStatus.Pending);

        var page = await QueryAsync($"search={paid.PaymentIntentId}");

        page.Items.Select(o => o.Id).Should().BeEquivalentTo(new[] { paid.Id });
    }

    /// <summary>
    /// An order id is matched exactly, so pasting one into the search box finds that order — and only
    /// that order, even though the id of an unrelated order is a perfectly good string.
    /// </summary>
    [Test]
    public async Task Search_MatchesAnOrderIdExactly()
    {
        var scope = NewScope();
        var wanted = await SeedAsync($"{scope}-a", 10m, 1, OrderStatus.Pending);
        await SeedAsync($"{scope}-b", 10m, 1, OrderStatus.Pending);

        var page = await QueryAsync($"search={wanted.Id}");

        page.Items.Select(o => o.Id).Should().BeEquivalentTo(new[] { wanted.Id });
    }

    /// <summary>
    /// A term full of LIKE metacharacters must be matched literally. Unescaped, <c>%</c> would match
    /// every order in the database — an admin's typo turning a search into a full table read.
    /// </summary>
    [Test]
    public async Task Search_TreatsWildcardsAsLiteralText()
    {
        var scope = NewScope();
        await SeedAsync($"{scope}-a", 10m, 1, OrderStatus.Pending);

        var page = await QueryAsync("search=%25"); // a URL-encoded literal percent sign

        page.Items.Should().BeEmpty("no user id contains a literal percent sign");
    }

    [Test]
    public async Task TheDateWindow_NarrowsToOrdersCreatedInside()
    {
        var scope = NewScope();
        var old = await SeedAsync($"{scope}-a", 10m, 1, OrderStatus.Pending,
            createdAt: new DateTime(2020, 1, 15, 12, 0, 0, DateTimeKind.Utc));
        var recent = await SeedAsync($"{scope}-b", 10m, 1, OrderStatus.Pending,
            createdAt: new DateTime(2020, 6, 15, 12, 0, 0, DateTimeKind.Utc));

        var page = await QueryAsync($"search={scope}&from=2020-05-01T00:00:00Z&to=2020-07-01T00:00:00Z");

        page.Items.Select(o => o.Id).Should().BeEquivalentTo(new[] { recent.Id });
        page.Items.Select(o => o.Id).Should().NotContain(old.Id);
    }

    /// <summary>
    /// <c>?from=2020-05-01</c> is what an admin URL actually looks like, and it binds to a DateTime
    /// with <c>Kind.Unspecified</c> — which Npgsql refuses to send as a <c>timestamptz</c> parameter.
    /// Without the handler reading it as UTC this is a 500, not a filter.
    /// </summary>
    [Test]
    public async Task ADateWithNoTimeZone_IsAcceptedAndReadAsUtc()
    {
        var scope = NewScope();
        var recent = await SeedAsync($"{scope}-a", 10m, 1, OrderStatus.Pending,
            createdAt: new DateTime(2020, 6, 15, 12, 0, 0, DateTimeKind.Utc));

        var page = await QueryAsync($"search={scope}&from=2020-05-01&to=2020-07-01");

        page.Items.Select(o => o.Id).Should().BeEquivalentTo(new[] { recent.Id });
    }

    [Test]
    public async Task TheTotalWindow_NarrowsToOrdersInsideIt()
    {
        var scope = NewScope();
        await SeedAsync($"{scope}-a", 5m, 1, OrderStatus.Pending);
        var middle = await SeedAsync($"{scope}-b", 50m, 1, OrderStatus.Pending);
        await SeedAsync($"{scope}-c", 500m, 1, OrderStatus.Pending);

        var page = await QueryAsync($"search={scope}&minTotal=20&maxTotal=100");

        page.Items.Select(o => o.Id).Should().BeEquivalentTo(new[] { middle.Id });
    }

    [Test]
    public async Task SortByTotalPrice_OrdersThePageByIt()
    {
        var scope = NewScope();
        await SeedAsync($"{scope}-a", 30m, 1, OrderStatus.Pending);
        await SeedAsync($"{scope}-b", 10m, 1, OrderStatus.Pending);
        await SeedAsync($"{scope}-c", 20m, 1, OrderStatus.Pending);

        var ascending = await QueryAsync($"search={scope}&sortBy=TotalPrice&isDescending=false");
        ascending.Items.Select(o => o.TotalPrice).Should().ContainInOrder(10m, 20m, 30m);

        var descending = await QueryAsync($"search={scope}&sortBy=totalprice&isDescending=true");
        descending.Items.Select(o => o.TotalPrice).Should().ContainInOrder(30m, 20m, 10m);
    }

    /// <summary>
    /// The default is newest first, and it must survive the sort parameter being optional — a request
    /// that names no sort is the one every existing caller makes.
    /// </summary>
    [Test]
    public async Task WithNoSortParameter_TheListIsStillNewestFirst()
    {
        var scope = NewScope();
        var older = await SeedAsync($"{scope}-a", 10m, 1, OrderStatus.Pending,
            createdAt: new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var newer = await SeedAsync($"{scope}-b", 10m, 1, OrderStatus.Pending,
            createdAt: new DateTime(2021, 2, 1, 0, 0, 0, DateTimeKind.Utc));

        var page = await QueryAsync($"search={scope}");

        page.Items.Select(o => o.Id).Should().ContainInOrder(newer.Id, older.Id);
    }

    [Test]
    public async Task AnUnknownSortColumn_IsRejected()
    {
        var response = await Client.GetAsync($"{OrdersEndpoint}?sortBy=shoeSize");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("SortBy");
    }

    [Test]
    public async Task AnInvertedDateRange_IsRejected_RatherThanAnsweredWithAnEmptyPage()
    {
        var response = await Client.GetAsync(
            $"{OrdersEndpoint}?from=2026-02-01T00:00:00Z&to=2026-01-01T00:00:00Z");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// The count must come from the same filter as the page. A total taken before the filter reports
    /// pages that do not exist, which is the shape audit M4 removed from the cursor path.
    /// </summary>
    [Test]
    public async Task TheTotalCount_IsTakenUnderTheSameFilter()
    {
        var scope = NewScope();
        await SeedAsync($"{scope}-a", 10m, 1, OrderStatus.Pending);
        await SeedAsync($"{scope}-b", 10m, 1, OrderStatus.Paid);
        await SeedAsync($"{scope}-c", 10m, 1, OrderStatus.Paid);

        var page = await QueryAsync($"search={scope}&statuses=Paid");

        page.TotalCount.Should().Be(2);
        page.Items.Should().HaveCount(2);
    }

    /// <summary>
    /// Every filter combines with every other. Written as one test because the risk is a filter
    /// silently replacing an earlier one rather than narrowing it further.
    /// </summary>
    [Test]
    public async Task TheFiltersCompose()
    {
        var scope = NewScope();
        var wanted = await SeedAsync($"{scope}-wanted", 40m, 1, OrderStatus.Paid,
            createdAt: new DateTime(2022, 6, 15, 12, 0, 0, DateTimeKind.Utc));
        await SeedAsync($"{scope}-wrong-status", 40m, 1, OrderStatus.Pending,
            createdAt: new DateTime(2022, 6, 15, 12, 0, 0, DateTimeKind.Utc));
        await SeedAsync($"{scope}-wrong-date", 40m, 1, OrderStatus.Paid,
            createdAt: new DateTime(2023, 6, 15, 12, 0, 0, DateTimeKind.Utc));
        await SeedAsync($"{scope}-wrong-total", 4000m, 1, OrderStatus.Paid,
            createdAt: new DateTime(2022, 6, 15, 12, 0, 0, DateTimeKind.Utc));

        var page = await QueryAsync(
            $"search={scope}&statuses=Paid&from=2022-01-01T00:00:00Z&to=2022-12-31T00:00:00Z&minTotal=10&maxTotal=100");

        page.Items.Select(o => o.Id).Should().BeEquivalentTo(new[] { wanted.Id });
    }
}
