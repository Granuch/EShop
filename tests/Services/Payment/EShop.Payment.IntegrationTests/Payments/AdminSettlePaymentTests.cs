using System.Net;
using System.Net.Http.Json;
using EShop.Payment.Domain.Entities;

namespace EShop.Payment.IntegrationTests.Payments;

/// <summary>
/// Payment audit Stage 3 (H4, D2). <c>POST /api/v1/payments</c> is an admin tool that settles an order's recorded
/// Pending payment through the simulator, for the recorded amount. The customer's 403 is in
/// <see cref="Security.NonAdminAuthorizationTests"/>.
/// </summary>
[TestFixture]
[Category("Integration")]
public class AdminSettlePaymentTests : AuthenticatedIntegrationTestBase
{
    private const string Endpoint = "/api/v1/payments";

    protected override string TestUserRole => "Admin";
    protected override string TestUserId => "admin-1";

    [Test]
    public async Task AnAdmin_SettlesTheRecordedPayment_ForTheRecordedAmount_WhateverTheRequestSays()
    {
        var seeded = await Factory.SeedPaymentAsync("customer-1", amount: 100m);

        // The fields the endpoint used to trust. They no longer bind to anything.
        var response = await Client.PostAsJsonAsync(Endpoint, new
        {
            seeded.OrderId,
            UserId = "someone-else",
            Amount = 1m,
            Currency = "JPY",
            PaymentMethod = "Stripe"
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<PaymentResponse>();
        Assert.Multiple(() =>
        {
            Assert.That(body!.Status, Is.EqualTo("SUCCESS"));
            Assert.That(body.Amount, Is.EqualTo(100m));
            Assert.That(body.Currency, Is.EqualTo("USD"));
            Assert.That(body.UserId, Is.EqualTo("customer-1"));
            Assert.That(body.PaymentMethod, Is.EqualTo("Mock"));
        });
    }

    [Test]
    public async Task AnOrderWithNoRecordedPayment_IsNotFound()
    {
        var orderId = Guid.NewGuid();

        var response = await Client.PostAsJsonAsync(Endpoint, new { OrderId = orderId });

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

        var response = await Client.PostAsJsonAsync(Endpoint, new { seeded.OrderId });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("PAYMENT_NOT_PENDING"));
        Assert.That((await Factory.FindByOrderIdAsync(seeded.OrderId))!.Status, Is.EqualTo(status));
    }

    [Test]
    public async Task AnEmptyOrderId_IsABadRequest()
    {
        var response = await Client.PostAsJsonAsync(Endpoint, new { OrderId = Guid.Empty });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    private sealed record PaymentResponse(
        Guid Id, Guid OrderId, string UserId, decimal Amount, string Currency, string PaymentMethod, string Status);
}
