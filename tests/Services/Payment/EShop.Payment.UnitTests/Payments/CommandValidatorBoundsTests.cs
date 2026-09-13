using System.Globalization;
using EShop.Payment.Application.Payments.Commands.CreatePaymentIntent;
using EShop.Payment.Application.Payments.Commands.RefundPayment;

namespace EShop.Payment.UnitTests.Payments;

/// <summary>
/// Payment audit Stage 10 (M6). The client-supplied values that reach Stripe or a column are bounded by validation, so
/// a bad one is a 400 naming the field. Until now it was a 500 from Stripe, or a 409 "conflict" from the database.
/// </summary>
[TestFixture]
public class CommandValidatorBoundsTests
{
    private static bool IntentIsValid(string? email)
        => new CreatePaymentIntentCommandValidator()
            .Validate(new CreatePaymentIntentCommand(Guid.NewGuid(), "user-1", false, email))
            .IsValid;

    private static bool RefundIsValid(decimal? amount, string? reason = null)
        => new RefundPaymentCommandValidator()
            .Validate(new RefundPaymentCommand(Guid.NewGuid(), amount, reason))
            .IsValid;

    [TestCase(null, true, Description = "the e-mail is optional")]
    [TestCase("", true)]
    [TestCase("   ", true, Description = "blank counts as none")]
    [TestCase("customer@example.test", true)]
    [TestCase("not-an-address", false)]
    public void ACreateIntentEmail(string? email, bool valid)
        => Assert.That(IntentIsValid(email), Is.EqualTo(valid));

    [Test]
    public void ACreateIntentEmail_IsBoundedByWhatSmtpAllows()
    {
        const string domain = "@example.test";

        Assert.Multiple(() =>
        {
            Assert.That(IntentIsValid(new string('a', 254 - domain.Length) + domain), Is.True, "254 characters");
            Assert.That(IntentIsValid(new string('a', 255 - domain.Length) + domain), Is.False, "255 characters");
        });
    }

    [TestCase(null, true, Description = "no amount means the full amount")]
    [TestCase("10", true)]
    [TestCase("10.1", true)]
    [TestCase("10.10", true)]
    [TestCase("10.100", true, Description = "trailing zeros are not precision")]
    [TestCase("10.001", false)]
    [TestCase("0", false)]
    public void ARefundAmount(string? amount, bool valid)
        => Assert.That(
            RefundIsValid(amount is null ? null : decimal.Parse(amount, CultureInfo.InvariantCulture)),
            Is.EqualTo(valid));

    [Test]
    public void ARefundReason_IsBoundedByTheColumnItWouldBeStoredIn()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RefundIsValid(null, new string('r', 500)), Is.True);
            Assert.That(RefundIsValid(null, new string('r', 501)), Is.False);
        });
    }
}
