using EShop.Identity.Application.Auth.Commands.ResendEmailConfirmation;

namespace EShop.Identity.UnitTests.Auth;

[TestFixture]
public class ResendEmailConfirmationCommandValidatorTests
{
    private readonly ResendEmailConfirmationCommandValidator _validator = new();

    [TestCase("")]
    [TestCase("not-an-email")]
    public async Task AMissingOrMalformedEmail_IsRefused(string email)
    {
        var result = await _validator.ValidateAsync(new ResendEmailConfirmationCommand { Email = email });

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Select(e => e.PropertyName), Is.All.EqualTo("Email"));
    }

    [Test]
    public async Task AnOverlongEmail_IsRefused()
    {
        var email = new string('a', 251) + "@x.com"; // 257 characters, one over the limit

        var result = await _validator.ValidateAsync(new ResendEmailConfirmationCommand { Email = email });

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public async Task AWellFormedEmail_IsAccepted()
    {
        var result = await _validator.ValidateAsync(new ResendEmailConfirmationCommand { Email = "user@test.com" });

        Assert.That(result.IsValid, Is.True);
    }
}
