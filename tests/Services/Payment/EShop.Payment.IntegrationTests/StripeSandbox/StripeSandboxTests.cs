using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;
using EShop.Payment.Infrastructure.Configuration;
using EShop.Payment.Infrastructure.Data;
using EShop.Payment.Infrastructure.Repositories;
using EShop.Payment.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Stripe;

namespace EShop.Payment.IntegrationTests.StripeSandbox;

/// <summary>
/// Ordering audit Stage 19. The automatic refund of a cancelled order depends on what Stripe does, so
/// these tests check it against the real Stripe sandbox instead of trusting a comment:
/// <list type="bullet">
///   <item>refunding an already-refunded charge under an idempotency key Stripe has not seen (for
///   example after the 24-hour window) is refused with <c>charge_already_refunded</c>, and our service
///   reports that as already refunded rather than failing;</item>
///   <item>our fixed key <c>refund-{intent}</c> replays the first refund within the window;</item>
///   <item>cancelling a captured intent is refused, and its status reads <c>succeeded</c>. That is the
///   signal the consumer refunds on.</item>
/// </list>
///
/// <para><b>These tests run only with a sandbox key.</b> Set <c>Stripe__SecretKey</c> (or
/// <c>STRIPE_SECRET_KEY</c>, as in <c>.env</c>) to an <c>sk_test_</c> key. Without one they are skipped, so a
/// clean checkout is unaffected; a live key is never used. Each run creates a few $10 test-mode payments,
/// all refunded, tagged <c>source=EShop.Payment.IntegrationTests</c>.</para>
///
/// <para><b>In CI they must run</b> (Stage 20). <c>ci.yml</c> passes the <c>STRIPE_SANDBOX_SECRET_KEY</c>
/// repository secret as <c>STRIPE_SECRET_KEY</c> and sets <c>STRIPE_SANDBOX_REQUIRED=true</c> for pushes and
/// same-repository pull requests; with that set, a missing or non-sandbox key fails the fixture instead of
/// skipping it, so a deleted secret turns CI red rather than quietly dropping the only tests that check
/// Stripe itself. Fork and Dependabot pull requests get no secrets from GitHub and are not required.</para>
/// </summary>
[TestFixture]
[Category("StripeSandbox")]
[NonParallelizable]
public class StripeSandboxTests
{
    private const long AmountMinor = 1000;
    private const decimal Amount = 10m;

    private string _key = null!;
    private StripeClient _client = null!;
    private StripePaymentService _service = null!;

    [OneTimeSetUp]
    public void RequireASandboxKey()
    {
        var key = Environment.GetEnvironmentVariable("Stripe__SecretKey");
        if (string.IsNullOrWhiteSpace(key))
        {
            key = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY");
        }

        if (string.IsNullOrWhiteSpace(key)
            || !key.StartsWith("sk_test_", StringComparison.Ordinal)
            || key.Contains("REPLACE", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(Environment.GetEnvironmentVariable("STRIPE_SANDBOX_REQUIRED"), "true", StringComparison.OrdinalIgnoreCase))
            {
                Assert.Fail("STRIPE_SANDBOX_REQUIRED is true, but Stripe__SecretKey / STRIPE_SECRET_KEY holds no sk_test_ key. "
                    + "In CI it comes from the STRIPE_SANDBOX_SECRET_KEY repository secret.");
            }

            Assert.Ignore("Stripe sandbox tests need Stripe__SecretKey (or STRIPE_SECRET_KEY) set to a real sk_test_ key.");
        }

        _key = key!;
        _client = new StripeClient(key);
        _service = new StripePaymentService(_client);
    }

    private async Task<PaymentIntent> ACapturedPaymentAsync()
    {
        var intent = await new PaymentIntentService(_client).CreateAsync(new PaymentIntentCreateOptions
        {
            Amount = AmountMinor,
            Currency = "usd",
            PaymentMethod = "pm_card_visa",
            PaymentMethodTypes = ["card"],
            Confirm = true,
            Description = "EShop Payment integration test (Ordering audit Stage 19)",
            Metadata = new Dictionary<string, string> { ["source"] = "EShop.Payment.IntegrationTests" }
        });

        Assert.That(intent.Status, Is.EqualTo("succeeded"), "precondition: a captured payment");
        return intent;
    }

    /// <summary>A refund made outside our service, under a key nobody will use again.</summary>
    private Task<Refund> RefundUnderAFreshKeyAsync(string paymentIntentId)
        => new RefundService(_client).CreateAsync(
            new RefundCreateOptions { PaymentIntent = paymentIntentId },
            new RequestOptions { IdempotencyKey = $"eshop-test-{Guid.NewGuid():N}" });

    /// <summary>The raw fact the "already refunded" handling rests on.</summary>
    [Test]
    public async Task ASecondFullRefund_UnderAKeyStripeHasNotSeen_IsRefusedAsChargeAlreadyRefunded()
    {
        var intent = await ACapturedPaymentAsync();
        await RefundUnderAFreshKeyAsync(intent.Id);

        var ex = Assert.ThrowsAsync<StripeException>(() => RefundUnderAFreshKeyAsync(intent.Id));

        Assert.That(ex!.StripeError?.Code, Is.EqualTo("charge_already_refunded"));
        Assert.That(StripePaymentService.IsAlreadyRefunded(ex), Is.True);
    }

    /// <summary>
    /// A refund committed at Stripe but lost here, retried after the window: the earlier refund stands in
    /// for one made under a key that has since expired. The service reports it as already refunded
    /// instead of throwing, so the retry records the refund instead of dead-lettering it.
    /// </summary>
    [Test]
    public async Task OurRefund_OfAPaymentAlreadyRefunded_ReportsAlreadyRefunded_InsteadOfFailing()
    {
        var intent = await ACapturedPaymentAsync();
        await RefundUnderAFreshKeyAsync(intent.Id);

        var result = await _service.CreateRefundAsync(intent.Id, Amount, "usd");

        Assert.That(result.AlreadyRefunded, Is.True);
        Assert.That(result.Status, Is.EqualTo("succeeded"));
    }

    /// <summary>Within the window our fixed key replays the first refund: one refund, however many retries.</summary>
    [Test]
    public async Task OurRefund_RepeatedWithinTheIdempotencyWindow_ReturnsTheSameRefund()
    {
        var intent = await ACapturedPaymentAsync();

        var first = await _service.CreateRefundAsync(intent.Id, Amount, "usd");
        var second = await _service.CreateRefundAsync(intent.Id, Amount, "usd");

        Assert.That(first.AlreadyRefunded, Is.False);
        Assert.That(first.Status, Is.EqualTo("succeeded"));
        Assert.That(second.RefundId, Is.EqualTo(first.RefundId));

        var refunds = await new RefundService(_client).ListAsync(new RefundListOptions { PaymentIntent = intent.Id });
        Assert.That(refunds.Data, Has.Count.EqualTo(1));
    }

    /// <summary>
    /// Stage 21 (D17), end to end with Stripe's own event. Our cancel tags the intent, and the
    /// <c>payment_intent.canceled</c> event Stripe then emits carries the tag, which is what our webhook
    /// parsing reads. The control is an intent cancelled outside our service: its event must not read as ours,
    /// or a Dashboard cancellation would be recorded Cancelled and Ordering never told.
    /// </summary>
    [Test]
    public async Task StripesCanceledEvent_CarriesOurTag_OnlyWhenWeCancelledTheIntent()
    {
        var ours = await AnUncapturedIntentAsync();
        var theirs = await AnUncapturedIntentAsync();

        var cancelled = await _service.CancelPaymentIntentAsync(ours.Id);
        await new PaymentIntentService(_client).CancelAsync(theirs.Id);
        Assert.That(cancelled.Status, Is.EqualTo("canceled"));

        var parser = new StripeWebhookEventParser(Options.Create(new StripeSettings
        {
            Enabled = true,
            SecretKey = _key,
            SkipWebhookSignatureVerification = true
        }));

        var ourEvent = parser.Parse((await CanceledEventForAsync(ours.Id)).ToJson(), string.Empty);
        var theirEvent = parser.Parse((await CanceledEventForAsync(theirs.Id)).ToJson(), string.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(ourEvent.Type, Is.EqualTo("payment_intent.canceled"));
            Assert.That(ourEvent.PaymentIntentId, Is.EqualTo(ours.Id));
            Assert.That(ourEvent.CancelRequestedByEShop, Is.True);
            Assert.That(theirEvent.PaymentIntentId, Is.EqualTo(theirs.Id));
            Assert.That(theirEvent.CancelRequestedByEShop, Is.False);
        });
    }

    /// <summary>
    /// The tag is written before the cancel, and Stripe may refuse to update an intent that is already
    /// canceled. Cancelling twice must still count as done, as it did before the tag existed.
    /// </summary>
    [Test]
    public async Task OurCancel_OfAnIntentAlreadyCanceled_StillCountsAsCanceled()
    {
        var intent = await AnUncapturedIntentAsync();
        await new PaymentIntentService(_client).CancelAsync(intent.Id);

        var result = await _service.CancelPaymentIntentAsync(intent.Id);

        Assert.That(result.Status, Is.EqualTo("canceled"));
    }

    private async Task<PaymentIntent> AnUncapturedIntentAsync()
        => await new PaymentIntentService(_client).CreateAsync(new PaymentIntentCreateOptions
        {
            Amount = AmountMinor,
            Currency = "usd",
            PaymentMethodTypes = ["card"],
            Description = "EShop Payment integration test (Ordering audit Stage 21)",
            Metadata = new Dictionary<string, string> { ["source"] = "EShop.Payment.IntegrationTests" }
        });

    /// <summary>
    /// Payment audit Stage 5 (H1, D3), with Stripe's own events. A declined card leaves the intent payable
    /// (<c>requires_payment_method</c>), and a second card pays the same intent. Fed those two events in order, our
    /// webhook keeps the payment open after the decline, with no <c>PaymentFailedEvent</c> (which would have Ordering
    /// cancel the order), then records the success.
    /// </summary>
    [Test]
    public async Task ADeclinedCard_LeavesTheIntentPayable_AndOurWebhookWaitsForTheSecondCard()
    {
        var intent = await AnUncapturedIntentAsync();
        var intents = new PaymentIntentService(_client);

        var declined = Assert.ThrowsAsync<StripeException>(() => intents.ConfirmAsync(
            intent.Id, new PaymentIntentConfirmOptions { PaymentMethod = "pm_card_chargeDeclined" }));
        Assert.That(declined!.StripeError?.Code, Is.EqualTo("card_declined"));
        Assert.That((await intents.GetAsync(intent.Id)).Status, Is.EqualTo("requires_payment_method"));
        var declineEvent = await EventForAsync("payment_intent.payment_failed", intent.Id);

        var paid = await intents.ConfirmAsync(intent.Id, new PaymentIntentConfirmOptions { PaymentMethod = "pm_card_visa" });
        Assert.That(paid.Status, Is.EqualTo("succeeded"), "the same intent takes a second card");
        var successEvent = await EventForAsync("payment_intent.succeeded", intent.Id);
        await RefundUnderAFreshKeyAsync(intent.Id);

        await using var db = new PaymentDbContext(new DbContextOptionsBuilder<PaymentDbContext>()
            .UseInMemoryDatabase($"sandbox-decline-{Guid.NewGuid():N}").Options);
        db.PaymentTransactions.Add(new PaymentTransaction
        {
            Id = Guid.NewGuid(),
            OrderId = Guid.NewGuid(),
            UserId = "sandbox-user",
            Amount = Amount,
            Currency = "USD",
            PaymentMethod = PaymentMethodType.Stripe,
            PaymentIntentId = intent.Id,
            Status = PaymentStatus.Processing,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var outbox = new Mock<IIntegrationEventOutbox>();
        var processor = new StripeWebhookProcessor(
            new PaymentRepository(db),
            new StripeWebhookEventParser(Options.Create(new StripeSettings { Enabled = true, SkipWebhookSignatureVerification = true })),
            db,
            outbox.Object,
            NullLogger<StripeWebhookProcessor>.Instance);

        await processor.ProcessAsync(declineEvent.ToJson(), string.Empty);

        var afterDecline = await db.PaymentTransactions.AsNoTracking().SingleAsync();
        Assert.Multiple(() =>
        {
            Assert.That(afterDecline.Status, Is.EqualTo(PaymentStatus.Processing));
            Assert.That(afterDecline.ErrorMessage, Is.Not.Null.And.Not.Empty, "Stripe's decline reason is recorded");
            Assert.That(afterDecline.StripeStatus, Is.EqualTo("requires_payment_method"));
        });
        outbox.Verify(o => o.Enqueue(It.IsAny<PaymentFailedEvent>(), It.IsAny<string?>()), Times.Never);

        await processor.ProcessAsync(successEvent.ToJson(), string.Empty);

        Assert.That((await db.PaymentTransactions.AsNoTracking().SingleAsync()).Status, Is.EqualTo(PaymentStatus.Success));
        outbox.Verify(o => o.Enqueue(It.IsAny<PaymentSuccessEvent>(), It.IsAny<string?>()), Times.Once);
    }

    /// <summary>
    /// Payment audit Stage 6 (M1), against Stripe itself. <c>/create-intent</c> creates the customer and the intent
    /// inside a database transaction that can roll back after Stripe has answered. Repeating our calls, as the retry
    /// does, must get back the same customer and the same intent, not a second of each.
    /// </summary>
    [Test]
    public async Task OurCustomerAndIntentCreation_Repeated_ReturnTheSameStripeObjects()
    {
        var userId = $"sandbox-{Guid.NewGuid():N}";
        var repository = new Mock<IPaymentRepository>();
        repository.Setup(r => r.GetCustomerByUserIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentCustomer?)null);
        repository.Setup(r => r.AddCustomerIfAbsentAsync(It.IsAny<PaymentCustomer>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentCustomer c, CancellationToken _) => c);
        var customers = new StripeCustomerService(repository.Object, _client);

        var customer = await customers.CreateOrGetCustomerAsync(userId, "sandbox@example.test");
        var customerAgain = await customers.CreateOrGetCustomerAsync(userId, "sandbox@example.test");

        var request = new StripePaymentIntentRequest(Guid.NewGuid(), Guid.NewGuid(), userId, customer, Amount, "USD");
        var intent = await _service.CreatePaymentIntentAsync(request);
        var intentAgain = await _service.CreatePaymentIntentAsync(request);

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(customerAgain, Is.EqualTo(customer));
                Assert.That(intentAgain.PaymentIntentId, Is.EqualTo(intent.PaymentIntentId));
            });
        }
        finally
        {
            await new PaymentIntentService(_client).CancelAsync(intent.PaymentIntentId);
            if (intentAgain.PaymentIntentId != intent.PaymentIntentId)
            {
                await new PaymentIntentService(_client).CancelAsync(intentAgain.PaymentIntentId);
            }

            await new CustomerService(_client).DeleteAsync(customer);
            if (customerAgain != customer)
            {
                await new CustomerService(_client).DeleteAsync(customerAgain);
            }
        }
    }

    /// <summary>Stripe writes events asynchronously, so poll briefly for the intent's canceled event.</summary>
    private Task<Event> CanceledEventForAsync(string paymentIntentId)
        => EventForAsync("payment_intent.canceled", paymentIntentId);

    private async Task<Event> EventForAsync(string type, string paymentIntentId)
    {
        var events = new EventService(_client);
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var page = await events.ListAsync(new EventListOptions { Type = type, Limit = 50 });
            var match = page.Data.FirstOrDefault(e => e.Data.Object is PaymentIntent pi && pi.Id == paymentIntentId);
            if (match is not null)
            {
                return match;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        Assert.Fail($"Stripe emitted no {type} event for {paymentIntentId} within 20 s.");
        return null!;
    }

    /// <summary>
    /// What the consumer sees in the race: Stripe refuses to cancel a captured intent, and the intent's
    /// status, read back, is exactly "succeeded". That value is what the consumer compares against.
    /// </summary>
    [Test]
    public async Task CancellingACapturedIntent_IsRefused_AndItsStatusReadsSucceeded()
    {
        var intent = await ACapturedPaymentAsync();
        try
        {
            Assert.ThrowsAsync<PaymentIntentNotCancellableException>(() => _service.CancelPaymentIntentAsync(intent.Id));
            Assert.That(await _service.GetPaymentIntentStatusAsync(intent.Id), Is.EqualTo("succeeded"));
        }
        finally
        {
            await RefundUnderAFreshKeyAsync(intent.Id);
        }
    }

    /// <summary>
    /// frontend-contracts F-47. When an order's items change after the customer opened the payment form, Payment changes
    /// the open intent's amount. Checked against Stripe: the update is accepted, the client secret still pays the same
    /// intent, and a card confirmed afterwards is charged the <b>new</b> amount.
    /// </summary>
    [Test]
    public async Task OurAmountUpdate_OfAnOpenIntent_IsWhatTheCardIsThenCharged()
    {
        var intents = new PaymentIntentService(_client);
        var intent = await AnUncapturedIntentAsync();
        PaymentIntent? paid = null;
        try
        {
            var status = await _service.UpdatePaymentIntentAmountAsync(intent.Id, 12.34m, "USD");
            var updated = await intents.GetAsync(intent.Id);

            paid = await intents.ConfirmAsync(intent.Id, new PaymentIntentConfirmOptions { PaymentMethod = "pm_card_visa" });

            Assert.Multiple(() =>
            {
                Assert.That(status, Is.EqualTo("requires_payment_method"));
                Assert.That(updated.Amount, Is.EqualTo(1234));
                Assert.That(updated.ClientSecret, Is.EqualTo(intent.ClientSecret), "the customer's open form keeps working");
                Assert.That(paid.Status, Is.EqualTo("succeeded"));
                Assert.That(paid.AmountReceived, Is.EqualTo(1234), "the card was charged the revised amount");
            });
        }
        finally
        {
            if (paid?.Status == "succeeded")
            {
                await RefundUnderAFreshKeyAsync(intent.Id);
            }
            else
            {
                await intents.CancelAsync(intent.Id);
            }
        }
    }

    /// <summary>
    /// frontend-contracts F-47. Once the card has been charged, Stripe refuses to change the amount, and our service
    /// reports that as <see cref="PaymentIntentNotUpdatableException"/> — the deterministic refusal the consumer
    /// dead-letters — rather than as a transient error it would retry.
    /// </summary>
    [Test]
    public async Task OurAmountUpdate_OfACapturedIntent_IsRefusedAsNotUpdatable()
    {
        var intent = await ACapturedPaymentAsync();
        try
        {
            Assert.ThrowsAsync<PaymentIntentNotUpdatableException>(
                () => _service.UpdatePaymentIntentAmountAsync(intent.Id, 12.34m, "USD"));
        }
        finally
        {
            await RefundUnderAFreshKeyAsync(intent.Id);
        }
    }

    /// <summary>frontend-contracts F-47. The same refusal for an intent that was cancelled.</summary>
    [Test]
    public async Task OurAmountUpdate_OfACanceledIntent_IsRefusedAsNotUpdatable()
    {
        var intent = await AnUncapturedIntentAsync();
        await new PaymentIntentService(_client).CancelAsync(intent.Id);

        Assert.ThrowsAsync<PaymentIntentNotUpdatableException>(
            () => _service.UpdatePaymentIntentAmountAsync(intent.Id, 12.34m, "USD"));
    }
}
