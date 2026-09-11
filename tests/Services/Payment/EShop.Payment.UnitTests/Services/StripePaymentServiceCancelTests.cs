using System.Net;
using EShop.Payment.Infrastructure.Services;
using Stripe;

namespace EShop.Payment.UnitTests.Services;

/// <summary>
/// Ordering audit Stage 9: how a Stripe error on cancel is classified decides whether a cancelled
/// order's payment is dead-lettered for a refund, retried, or treated as done. It keys on Stripe's
/// structured error code, never the message text.
/// </summary>
[TestFixture]
public class StripePaymentServiceCancelTests
{
    private static StripeException Error(string code, string? intentStatus = null) => new(
        HttpStatusCode.BadRequest,
        new StripeError
        {
            Code = code,
            Message = "This PaymentIntent's status is " + (intentStatus ?? "unknown"),
            PaymentIntent = intentStatus is null ? null : new PaymentIntent { Status = intentStatus }
        },
        "stripe error");

    [Test]
    public void AnIntentThatAlreadySucceeded_IsNotCancellable_AndNotAlreadyCanceled()
    {
        var ex = Error("payment_intent_unexpected_state", "succeeded");

        Assert.That(StripePaymentService.IsUnexpectedState(ex), Is.True);
        Assert.That(StripePaymentService.IsAlreadyCanceled(ex), Is.False);
    }

    [Test]
    public void AnIntentAlreadyCanceled_CountsAsDone()
    {
        Assert.That(StripePaymentService.IsAlreadyCanceled(Error("payment_intent_unexpected_state", "canceled")), Is.True);
    }

    [TestCase("api_error")]
    [TestCase("rate_limit")]
    [TestCase("resource_missing")]
    public void AnyOtherStripeError_IsNeitherRefusalNorDone_SoItPropagatesAndIsRetried(string code)
    {
        var ex = Error(code, "requires_payment_method");

        Assert.That(StripePaymentService.IsUnexpectedState(ex), Is.False);
        Assert.That(StripePaymentService.IsAlreadyCanceled(ex), Is.False);
    }
}
