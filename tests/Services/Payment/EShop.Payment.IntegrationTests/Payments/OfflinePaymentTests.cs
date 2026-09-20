using System.Net;
using System.Net.Http.Json;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Payment.IntegrationTests.Payments;

/// <summary>
/// Admin panel S10 (endpoint #58, decision Q6a). <c>POST /api/v1/payments/offline</c> records an order paid by
/// transfer or cash. The customer's 403 is in <see cref="Security.NonAdminAuthorizationTests"/>.
/// <para>Every refusal here reads the stored row back, because a 409 is returned whether or not the write rolled
/// back — <c>TransactionBehavior</c> commits on a <c>Result</c> failure too.</para>
/// </summary>
[TestFixture]
[Category("Integration")]
public class OfflinePaymentTests : AuthenticatedIntegrationTestBase
{
    private const string Endpoint = "/api/v1/payments/offline";

    protected override string TestUserRole => "Admin";
    protected override string TestUserId => "admin-1";

    private async Task<List<string>> EnqueuedEventTypesAsync(Guid orderId)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        return await db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.Payload.Contains(orderId.ToString()))
            .Select(m => m.Type)
            .ToListAsync();
    }

    [Test]
    public async Task AnOrderPaidOffline_IsSettledForTheRecordedAmount()
    {
        var seeded = await Factory.SeedPaymentAsync("customer-1", amount: 250m);

        var response = await Client.PostAsJsonAsync(Endpoint, new { seeded.OrderId, Reference = "TRF-2026-0042" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<PaymentResponse>();
        Assert.Multiple(() =>
        {
            Assert.That(body!.Status, Is.EqualTo("SUCCESS"));
            Assert.That(body.Amount, Is.EqualTo(250m));
            Assert.That(body.Currency, Is.EqualTo("USD"));
            Assert.That(body.PaymentMethod, Is.EqualTo("Mock"));
            Assert.That(body.PaymentIntentId, Is.EqualTo("offline:TRF-2026-0042"));
        });
    }

    /// <summary>
    /// The whole point of Q6a: Ordering gains no second way to mark an order paid. The fact travels as the
    /// <c>PaymentSuccessEvent</c> that <c>PaymentSuccessConsumer</c> already turns into <c>Order.MarkAsPaid</c>, with
    /// the amount and currency it checks — and it carries the operator's reference, so the order records what it was
    /// paid against.
    /// </summary>
    [Test]
    public async Task ASettlement_EnqueuesTheEventsOrderingAndNotificationConsume()
    {
        var seeded = await Factory.SeedPaymentAsync("customer-1", amount: 99m);

        await Client.PostAsJsonAsync(Endpoint, new { seeded.OrderId, Reference = "TRF-1" });

        var types = await EnqueuedEventTypesAsync(seeded.OrderId);
        Assert.Multiple(() =>
        {
            Assert.That(types, Does.Contain(typeof(PaymentSuccessEvent).FullName));
            Assert.That(types, Does.Contain(typeof(PaymentCompletedEvent).FullName));
            Assert.That(types, Does.Not.Contain(typeof(PaymentCreatedEvent).FullName),
                "nothing started — the money had already arrived, and PaymentCreatedEvent emails \"Payment started\"");
        });
    }

    [Test]
    public async Task AnOrderWithNoRecordedPayment_IsNotFound_AndCreatesNothing()
    {
        var orderId = Guid.NewGuid();

        var response = await Client.PostAsJsonAsync(Endpoint, new { OrderId = orderId, Reference = "TRF-1" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await Factory.FindByOrderIdAsync(orderId), Is.Null, "nothing may be created from the request");
    }

    [TestCase(PaymentStatus.Processing)]
    [TestCase(PaymentStatus.Success)]
    [TestCase(PaymentStatus.Failed)]
    [TestCase(PaymentStatus.Refunded)]
    [TestCase(PaymentStatus.Cancelled)]
    public async Task APaymentThatIsNotPending_IsAConflict_AndUnchanged(PaymentStatus status)
    {
        var seeded = await Factory.SeedPaymentAsync("customer-1", status);

        var response = await Client.PostAsJsonAsync(Endpoint, new { seeded.OrderId, Reference = "TRF-1" });

        var stored = (await Factory.FindByOrderIdAsync(seeded.OrderId))!;
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(stored.Status, Is.EqualTo(status));
            Assert.That(stored.PaymentIntentId, Is.Empty);
        });
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("PAYMENT_NOT_PENDING"));
    }

    /// <summary>
    /// A Pending Stripe payment that already carries an intent is the one case the status check cannot catch on its
    /// own, and settling it would strand an intent the customer can still pay.
    /// </summary>
    [Test]
    public async Task APendingPaymentWithALiveStripeIntent_IsRefused()
    {
        var seeded = await Factory.SeedPaymentAsync("customer-1", intentId: "pi_live_99");

        var response = await Client.PostAsJsonAsync(Endpoint, new { seeded.OrderId, Reference = "TRF-1" });

        Assert.That((int)response.StatusCode, Is.GreaterThanOrEqualTo(400));
        var stored = (await Factory.FindByOrderIdAsync(seeded.OrderId))!;
        Assert.Multiple(() =>
        {
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Pending));
            Assert.That(stored.PaymentIntentId, Is.EqualTo("pi_live_99"));
        });
    }

    [Test]
    public async Task AReferenceAlreadyRecorded_IsAConflict_AndChangesNothing()
    {
        var first = await Factory.SeedPaymentAsync("customer-1");
        var firstResponse = await Client.PostAsJsonAsync(Endpoint, new { first.OrderId, Reference = "TRF-DUP" });
        Assert.That(firstResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var second = await Factory.SeedPaymentAsync("customer-2");
        var response = await Client.PostAsJsonAsync(Endpoint, new { second.OrderId, Reference = "TRF-DUP" });

        var body = await response.Content.ReadAsStringAsync();
        var stored = (await Factory.FindByOrderIdAsync(second.OrderId))!;
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
            Assert.That(body, Does.Contain("PAYMENT_REFERENCE_IN_USE"));
            Assert.That(body, Does.Contain(first.OrderId.ToString()),
                "the detail must name the order that holds the reference; \"duplicate, retry\" can never succeed here");
            Assert.That(stored.Status, Is.EqualTo(PaymentStatus.Pending));
        });
    }

    [Test]
    public async Task AReference_IsTrimmedBeforeItIsStored()
    {
        var seeded = await Factory.SeedPaymentAsync("customer-1");

        var response = await Client.PostAsJsonAsync(Endpoint, new { seeded.OrderId, Reference = "  TRF-SPACED  " });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await Factory.FindByOrderIdAsync(seeded.OrderId))!.PaymentIntentId,
            Is.EqualTo("offline:TRF-SPACED"), "the stored reference is trimmed");
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public async Task AMissingReference_IsABadRequest(string? reference)
    {
        var seeded = await Factory.SeedPaymentAsync("customer-1");

        var response = await Client.PostAsJsonAsync(Endpoint, new { seeded.OrderId, Reference = reference });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await Factory.FindByOrderIdAsync(seeded.OrderId))!.Status, Is.EqualTo(PaymentStatus.Pending));
    }

    [Test]
    public async Task AReferenceLongerThanTheColumnAllows_IsABadRequest()
    {
        var seeded = await Factory.SeedPaymentAsync("customer-1");

        var response = await Client.PostAsJsonAsync(
            Endpoint, new { seeded.OrderId, Reference = new string('x', 101) });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task AnEmptyOrderId_IsABadRequest()
    {
        var response = await Client.PostAsJsonAsync(Endpoint, new { OrderId = Guid.Empty, Reference = "TRF-1" });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    /// <summary>
    /// There is no amount on the request, and sending one must change nothing: an operator-supplied amount would reach
    /// <c>Order.MarkAsPaid</c>, which throws on any mismatch, so a typo would dead-letter the message in Ordering long
    /// after this request answered 200.
    /// </summary>
    [Test]
    public async Task AnAmountInTheRequest_IsIgnored()
    {
        var seeded = await Factory.SeedPaymentAsync("customer-1", amount: 100m);

        var response = await Client.PostAsJsonAsync(Endpoint, new
        {
            seeded.OrderId,
            Reference = "TRF-1",
            Amount = 1m,
            Currency = "JPY",
            UserId = "someone-else"
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var stored = (await Factory.FindByOrderIdAsync(seeded.OrderId))!;
        Assert.Multiple(() =>
        {
            Assert.That(stored.Amount, Is.EqualTo(100m));
            Assert.That(stored.Currency, Is.EqualTo("USD"));
            Assert.That(stored.UserId, Is.EqualTo("customer-1"));
        });
    }

    private sealed record PaymentResponse(
        Guid Id,
        Guid OrderId,
        string UserId,
        decimal Amount,
        string Currency,
        string PaymentMethod,
        string Status,
        string? PaymentIntentId);
}
