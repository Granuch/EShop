using EShop.BuildingBlocks.Domain;
using EShop.Payment.Application.Payments.Commands.CreatePaymentIntent;

namespace EShop.Payment.UnitTests.Payments;

/// <summary>
/// Payment audit Stage 12. <c>LoggingBehavior</c> logs every command at Information. It redacts a property only when
/// the property carries <c>[SensitiveData]</c> or its name is on a fixed list of secret names. "Email" is on neither,
/// so the create-intent e-mail, which is personal data, went to the log in clear text. This follows Ordering's
/// <c>CommandLoggingTests</c>.
/// </summary>
[TestFixture]
public class CommandLoggingTests
{
    [Test]
    public void TheCreateIntentEmail_IsRedactedFromTheLog()
    {
        var property = typeof(CreatePaymentIntentCommand).GetProperty(nameof(CreatePaymentIntentCommand.Email));

        Assert.That(property!.IsDefined(typeof(SensitiveDataAttribute), inherit: true), Is.True,
            "CreatePaymentIntentCommand.Email must carry [SensitiveData]; on a positional record that takes [property: SensitiveData]");
    }
}
