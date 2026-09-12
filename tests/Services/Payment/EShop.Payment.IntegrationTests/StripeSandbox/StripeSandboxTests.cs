using EShop.Payment.Application.Payments.Abstractions;
using EShop.Payment.Infrastructure.Configuration;
using EShop.Payment.Infrastructure.Services;
using Microsoft.Extensions.Options;
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

        _client = new StripeClient(key);
        _service = new StripePaymentService(Options.Create(new StripeSettings { Enabled = true, SecretKey = key! }));
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
}
