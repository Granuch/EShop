using System.Net;
using EShop.Payment.Infrastructure.Services;
using Stripe;

namespace EShop.Payment.UnitTests.Services;

/// <summary>
/// Ordering audit Stage 19: which Stripe refund error means "the money is already back". It keys on
/// Stripe's structured code; that Stripe really sends this code is checked against the sandbox by
/// <c>StripeSandboxTests</c> in the integration project.
/// </summary>
[TestFixture]
public class StripePaymentServiceRefundTests
{
    private static StripeException Error(string code) => new(
        HttpStatusCode.BadRequest,
        new StripeError { Code = code, Message = "stripe says no" },
        "stripe error");

    [Test]
    public void AChargeAlreadyRefunded_CountsAsRefunded()
    {
        Assert.That(StripePaymentService.IsAlreadyRefunded(Error("charge_already_refunded")), Is.True);
    }

    [TestCase("charge_disputed")]
    [TestCase("amount_too_large")]
    [TestCase("payment_intent_unexpected_state")]
    [TestCase("api_error")]
    public void AnyOtherRefundError_IsNot(string code)
    {
        Assert.That(StripePaymentService.IsAlreadyRefunded(Error(code)), Is.False);
    }
}
