using System.Net;
using System.Net.Http.Json;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Payment.IntegrationTests.Payments;

/// <summary>
/// Admin panel S10 (endpoint #65). <c>GET /api/v1/payments</c> — the first cross-user payment list this service has
/// ever had. The customer's 403 is in <see cref="Security.NonAdminAuthorizationTests"/>.
/// <para>Each test gets its own host and its own in-memory database (see <c>IntegrationTestBase</c>), so a test may
/// assert on the whole list rather than having to scope itself to its own rows.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class AdminPaymentListTests : AuthenticatedIntegrationTestBase
{
    protected override string TestUserRole => "Admin";
    protected override string TestUserId => "admin-1";

    private static readonly DateTime Base = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    private async Task<PaymentTransaction> SeedAsync(
        string userId = "customer-1",
        PaymentStatus status = PaymentStatus.Success,
        PaymentMethodType method = PaymentMethodType.Stripe,
        decimal amount = 100m,
        string currency = "USD",
        DateTime? createdAt = null)
    {
        // Held in a local. SetAuditFields mutates the tracked instance during the save below, so reading
        // payment.CreatedAt afterwards gives the save time rather than the date this test asked for.
        var stamp = createdAt ?? Base;

        var payment = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = userId,
            Amount = amount,
            Currency = currency,
            PaymentMethod = method,
            PaymentIntentId = $"pi_{Guid.NewGuid():N}",
            Status = status,
            CreatedAt = stamp,
            UpdatedAt = stamp
        };
        await Factory.SeedAsync(payment);

        // BaseDbContext stamps CreatedAt with the save time on insert, whatever the entity held, so the date a test
        // means to filter on is set by a second save — an update stamps only UpdatedAt. Without this every date test
        // would silently be testing insertion order.
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var stored = await db.PaymentTransactions.SingleAsync(p => p.Id == payment.Id);
        stored.CreatedAt = stamp;
        await db.SaveChangesAsync();

        return payment;
    }

    private async Task<Page> ListAsync(string query = "")
    {
        var response = await Client.GetAsync($"/api/v1/payments{query}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Page>())!;
    }

    private static IEnumerable<Guid> Ids(Page page) => page.Items.Select(p => p.Id);

    // ---- Shape ----

    [Test]
    public async Task ThePaymentsOfEveryUser_ArePagedNewestFirst()
    {
        var oldest = await SeedAsync("customer-1", createdAt: Base.AddHours(-3));
        var newest = await SeedAsync("customer-2", createdAt: Base.AddHours(-1));
        var middle = await SeedAsync("customer-3", createdAt: Base.AddHours(-2));

        var first = await ListAsync("?pageNumber=1&pageSize=2");
        var second = await ListAsync("?pageNumber=2&pageSize=2");

        Assert.Multiple(() =>
        {
            Assert.That(Ids(first), Is.EqualTo(new[] { newest.Id, middle.Id }));
            Assert.That(first.TotalCount, Is.EqualTo(3));
            Assert.That(first.HasNextPage, Is.True);
            Assert.That(Ids(second), Is.EqualTo(new[] { oldest.Id }));
            Assert.That(second.HasNextPage, Is.False);
        });
    }

    [Test]
    public async Task WithNoPageGiven_TheNewestTenAreReturned()
    {
        for (var i = 0; i < 11; i++)
        {
            await SeedAsync(createdAt: Base.AddMinutes(-i));
        }

        var page = await ListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(page.Items, Has.Count.EqualTo(10));
            Assert.That(page.TotalCount, Is.EqualTo(11));
            Assert.That(page.PageSize, Is.EqualTo(10));
        });
    }

    /// <summary>
    /// The opposite of the customer list, deliberately. <c>PaymentRepository.ForUserList</c> hides the
    /// method-<c>None</c> placeholder a cancellation leaves when it overtakes the order, because nothing was ever
    /// started or charged for it — but that row is exactly what an operator opens this screen to find, since it is
    /// what stops a late <c>OrderCreatedEvent</c> charging a cancelled order.
    /// </summary>
    [Test]
    public async Task ThePlaceholderOfAnOrderCancelledBeforeItsPayment_IsListedHere()
    {
        var paid = await SeedAsync();
        var placeholder = PaymentTransaction.RecordCancelledBeforeCreation(
            Guid.NewGuid(), "customer-9", "Order cancelled before its payment was recorded.", Base);
        await Factory.SeedAsync(placeholder);

        var page = await ListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(Ids(page), Does.Contain(placeholder.Id));
            Assert.That(Ids(page), Does.Contain(paid.Id));
            Assert.That(page.TotalCount, Is.EqualTo(2));
        });
    }

    // ---- Filters ----

    [Test]
    public async Task Filter_Status_NarrowsToThatStatus()
    {
        var success = await SeedAsync(status: PaymentStatus.Success);
        await SeedAsync(status: PaymentStatus.Failed);

        var page = await ListAsync("?status=Success");

        Assert.That(Ids(page), Is.EqualTo(new[] { success.Id }));
    }

    /// <summary>Repeatable, and a union — the panel's status multi-select is one request.</summary>
    [Test]
    public async Task Filter_Status_IsRepeatable_AndUnions()
    {
        var success = await SeedAsync(status: PaymentStatus.Success, createdAt: Base);
        var refunded = await SeedAsync(status: PaymentStatus.Refunded, createdAt: Base.AddMinutes(-1));
        await SeedAsync(status: PaymentStatus.Failed);

        var page = await ListAsync("?status=Success&status=Refunded");

        Assert.Multiple(() =>
        {
            Assert.That(Ids(page), Is.EqualTo(new[] { success.Id, refunded.Id }));
            Assert.That(page.TotalCount, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task Filter_UserId_NarrowsToThatUser()
    {
        var mine = await SeedAsync("customer-1");
        await SeedAsync("customer-2");

        var page = await ListAsync("?userId=customer-1");

        Assert.That(Ids(page), Is.EqualTo(new[] { mine.Id }));
    }

    [Test]
    public async Task Filter_OrderId_AnswersAtMostOnePayment()
    {
        var target = await SeedAsync();
        await SeedAsync();

        var page = await ListAsync($"?orderId={target.OrderId}");

        Assert.Multiple(() =>
        {
            Assert.That(Ids(page), Is.EqualTo(new[] { target.Id }));
            Assert.That(page.TotalCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Filter_PaymentMethod_NarrowsToThatMethod()
    {
        var mock = await SeedAsync(method: PaymentMethodType.Mock);
        await SeedAsync(method: PaymentMethodType.Stripe);

        var page = await ListAsync("?paymentMethod=Mock");

        Assert.That(Ids(page), Is.EqualTo(new[] { mock.Id }));
    }

    [Test]
    public async Task Filter_AmountRange_IsInclusiveAtBothEnds()
    {
        var low = await SeedAsync(amount: 10m, createdAt: Base);
        var high = await SeedAsync(amount: 100m, createdAt: Base.AddMinutes(-1));
        await SeedAsync(amount: 500m);

        var page = await ListAsync("?minAmount=10&maxAmount=100");

        Assert.That(Ids(page), Is.EquivalentTo(new[] { low.Id, high.Id }));
    }

    [Test]
    public async Task Filter_DateRange_IsInclusiveAtBothEnds()
    {
        var inside = await SeedAsync(createdAt: Base);
        var onTheEdge = await SeedAsync(createdAt: Base.AddDays(-1));
        await SeedAsync(createdAt: Base.AddDays(-5));

        var page = await ListAsync($"?from={Base.AddDays(-1):O}&to={Base:O}");

        Assert.That(Ids(page), Is.EquivalentTo(new[] { inside.Id, onTheEdge.Id }));
    }

    /// <summary>
    /// <c>?from=2026-09-09</c> is what an admin URL actually looks like. It binds with
    /// <c>DateTimeKind.Unspecified</c>, which Npgsql refuses to send as a <c>timestamptz</c> parameter — so without
    /// the handler's UTC coercion this is a 500 rather than a filter.
    /// </summary>
    [Test]
    public async Task Filter_ADateWithNoTimeZone_IsAcceptedAndReadAsUtc()
    {
        var inside = await SeedAsync(createdAt: Base);
        await SeedAsync(createdAt: Base.AddDays(-5));

        var page = await ListAsync("?from=2026-09-09");

        Assert.That(Ids(page), Is.EqualTo(new[] { inside.Id }));
    }

    [Test]
    public async Task Filters_Compose()
    {
        var wanted = await SeedAsync("customer-1", PaymentStatus.Success, PaymentMethodType.Mock, 50m);
        await SeedAsync("customer-1", PaymentStatus.Failed, PaymentMethodType.Mock, 50m);
        await SeedAsync("customer-2", PaymentStatus.Success, PaymentMethodType.Mock, 50m);
        await SeedAsync("customer-1", PaymentStatus.Success, PaymentMethodType.Stripe, 50m);
        await SeedAsync("customer-1", PaymentStatus.Success, PaymentMethodType.Mock, 5000m);

        var page = await ListAsync("?userId=customer-1&status=Success&paymentMethod=Mock&maxAmount=100");

        Assert.Multiple(() =>
        {
            Assert.That(Ids(page), Is.EqualTo(new[] { wanted.Id }));
            Assert.That(page.TotalCount, Is.EqualTo(1));
        });
    }

    // ---- Refusals ----

    [TestCase("?status=Payed")]
    [TestCase("?status=Success&status=Payed")]
    [TestCase("?paymentMethod=Cheque")]
    [TestCase("?pageSize=0")]
    [TestCase("?pageSize=101")]
    [TestCase("?pageNumber=0")]
    [TestCase("?minAmount=50&maxAmount=5")]
    [TestCase("?from=2026-09-10T00:00:00Z&to=2026-09-01T00:00:00Z")]
    public async Task AnUnusableFilter_IsABadRequest(string query)
    {
        var response = await Client.GetAsync($"/api/v1/payments{query}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    private sealed record Page(
        List<PaymentRow> Items,
        int PageNumber,
        int PageSize,
        int TotalCount,
        int TotalPages,
        bool HasPreviousPage,
        bool HasNextPage);

    private sealed record PaymentRow(
        Guid Id, Guid OrderId, string UserId, decimal Amount, string Currency, string PaymentMethod, string Status);
}
