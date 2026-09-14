using System.Net;
using System.Net.Http.Json;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Domain.Entities;
using EShop.BuildingBlocks.Domain.Outbox;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace EShop.Payment.IntegrationTests.Payments;

/// <summary>
/// Payment audit Stage 2 (C2, D4), over HTTP: the customer names the order, and Stripe is asked for what Payment
/// recorded for it. Before, the client chose the amount and the currency, so 100 JPY could pay a $100 order.
/// </summary>
[TestFixture]
[Category("Integration")]
public class CreatePaymentIntentTests : AuthenticatedIntegrationTestBase
{
    private const string Endpoint = "/api/v1/payments/create-intent";

    private StripeEnabledPaymentApiFactory Stripe => (StripeEnabledPaymentApiFactory)Factory;

    protected override PaymentApiFactory CreateFactory() => new StripeEnabledPaymentApiFactory();

    private async Task<PaymentTransaction> SeedAsync(
        string? userId = null,
        PaymentStatus status = PaymentStatus.Pending,
        string intentId = "")
    {
        var payment = new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = userId ?? TestUserId,
            Amount = 100m,
            Currency = "USD",
            PaymentMethod = PaymentMethodType.Stripe,
            PaymentIntentId = intentId,
            Status = status,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await Stripe.SeedAsync(payment);
        return payment;
    }

    private void VerifyStripeNeverAsked() => Stripe.Stripe.Verify(
        x => x.CreatePaymentIntentAsync(It.IsAny<StripePaymentIntentRequest>(), It.IsAny<CancellationToken>()),
        Times.Never);

    [Test]
    public async Task ACustomer_PaysTheRecordedTotal_WhateverTheRequestSays()
    {
        var seeded = await SeedAsync();
        StripePaymentIntentRequest? sent = null;
        Stripe.StripeCustomers
            .Setup(x => x.CreateOrGetCustomerAsync(TestUserId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("cus_1");
        Stripe.Stripe
            .Setup(x => x.CreatePaymentIntentAsync(It.IsAny<StripePaymentIntentRequest>(), It.IsAny<CancellationToken>()))
            .Callback<StripePaymentIntentRequest, CancellationToken>((r, _) => sent = r)
            .ReturnsAsync(new StripePaymentIntentResult("pi_1", "cs_1", "requires_payment_method"));

        // The fields the endpoint used to trust. They no longer bind to anything.
        var response = await Client.PostAsJsonAsync(Endpoint, new
        {
            seeded.OrderId,
            UserId = "someone-else",
            Amount = 1m,
            Currency = "JPY",
            Email = "user@test.com"
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.Multiple(() =>
        {
            Assert.That(sent!.Amount, Is.EqualTo(100m));
            Assert.That(sent.Currency, Is.EqualTo("USD"));
            Assert.That(sent.UserId, Is.EqualTo(TestUserId));
        });

        var body = await response.Content.ReadFromJsonAsync<IntentResponse>();
        Assert.That(body!.ClientSecret, Is.EqualTo("cs_1"));

        var stored = await Stripe.FindByOrderIdAsync(seeded.OrderId);
        Assert.Multiple(() =>
        {
            Assert.That(stored!.Status, Is.EqualTo(PaymentStatus.Processing));
            Assert.That(stored.PaymentIntentId, Is.EqualTo("pi_1"));
        });
    }

    [Test]
    public async Task BeforeTheOrdersPaymentIsRecorded_ItIsNotReady()
    {
        var orderId = Guid.NewGuid();

        var response = await Client.PostAsJsonAsync(Endpoint, new { OrderId = orderId });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("PAYMENT_NOT_READY"));
        Assert.That(await Stripe.FindByOrderIdAsync(orderId), Is.Null);
        VerifyStripeNeverAsked();
    }

    [Test]
    public async Task AnotherCustomersOrder_IsNotFound_AndNotTouched()
    {
        var seeded = await SeedAsync(userId: "customer-2");

        var response = await Client.PostAsJsonAsync(Endpoint, new { seeded.OrderId });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var stored = await Stripe.FindByOrderIdAsync(seeded.OrderId);
        Assert.Multiple(() =>
        {
            Assert.That(stored!.Status, Is.EqualTo(PaymentStatus.Pending));
            Assert.That(stored.PaymentIntentId, Is.Empty);
        });
        VerifyStripeNeverAsked();
    }

    [Test]
    public async Task APaymentAlreadyStarted_IsAConflict()
    {
        var seeded = await SeedAsync(status: PaymentStatus.Processing, intentId: "pi_existing");

        var response = await Client.PostAsJsonAsync(Endpoint, new { seeded.OrderId });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("PAYMENT_ALREADY_EXISTS"));
        Assert.That((await Stripe.FindByOrderIdAsync(seeded.OrderId))!.PaymentIntentId, Is.EqualTo("pi_existing"));
        VerifyStripeNeverAsked();
    }

    /// <summary>
    /// The order was cancelled before the customer began paying, so its Stripe payment is Cancelled with no intent.
    /// Starting it must not open a live intent for an order that no longer exists.
    /// </summary>
    [Test]
    public async Task ACancelledOrdersPayment_CannotBeStarted()
    {
        var seeded = await SeedAsync(status: PaymentStatus.Cancelled);

        var response = await Client.PostAsJsonAsync(Endpoint, new { seeded.OrderId });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That((await Stripe.FindByOrderIdAsync(seeded.OrderId))!.Status, Is.EqualTo(PaymentStatus.Cancelled));
        VerifyStripeNeverAsked();
    }

    /// <summary>
    /// Payment audit Stage 6 (H2), over HTTP. A Stripe outage used to be recorded as the payment failing, with a
    /// PaymentFailedEvent, so Ordering cancelled the order and every later attempt was a conflict.
    /// </summary>
    [Test]
    public async Task AnUnavailableProvider_Is503_RecordsNothing_AndTheSameRequestThenSucceeds()
    {
        var seeded = await SeedAsync();
        Stripe.StripeCustomers
            .Setup(x => x.CreateOrGetCustomerAsync(TestUserId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("cus_1");
        Stripe.Stripe
            .SetupSequence(x => x.CreatePaymentIntentAsync(It.IsAny<StripePaymentIntentRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new PaymentProviderUnavailableException("create payment intent", new HttpRequestException("timeout")))
            .ReturnsAsync(new StripePaymentIntentResult("pi_retry", "cs_retry", "requires_payment_method"));

        var unavailable = await Client.PostAsJsonAsync(Endpoint, new { seeded.OrderId });

        Assert.That(unavailable.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(await unavailable.Content.ReadAsStringAsync(), Does.Contain("PAYMENT_PROVIDER_UNAVAILABLE"));
        var untouched = await Stripe.FindByOrderIdAsync(seeded.OrderId);
        Assert.Multiple(() =>
        {
            Assert.That(untouched!.Status, Is.EqualTo(PaymentStatus.Pending));
            Assert.That(untouched.PaymentIntentId, Is.Empty);
            Assert.That(OutboxRowsFor(seeded.OrderId, "PaymentFailedEvent"), Is.Zero);
        });

        var retried = await Client.PostAsJsonAsync(Endpoint, new { seeded.OrderId });

        Assert.That(retried.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await Stripe.FindByOrderIdAsync(seeded.OrderId))!.PaymentIntentId, Is.EqualTo("pi_retry"));
    }

    private int OutboxRowsFor(Guid orderId, string eventName)
    {
        using var scope = Stripe.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var order = orderId.ToString();
        return db.Set<OutboxMessage>().AsNoTracking().Count(m => m.Type.Contains(eventName) && m.Payload.Contains(order));
    }

    private sealed record IntentResponse(Guid PaymentId, string PaymentIntentId, string ClientSecret, string Status);
}
