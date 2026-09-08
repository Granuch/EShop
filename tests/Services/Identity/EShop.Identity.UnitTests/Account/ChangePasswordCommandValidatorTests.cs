using EShop.Identity.Application.Account.Commands.ChangePassword;

namespace EShop.Identity.UnitTests.Account;

/// <summary>
/// TEST-06. The password policy is enforced in two unrelated places and they can drift: this
/// validator, and ASP.NET Identity's own <c>PasswordOptions</c> configured in
/// <c>ServiceCollectionExtensions</c>. The validator is the one that produces a useful 400 —
/// Identity's rejection arrives later as an <c>IdentityResult</c> failure — so a rule quietly
/// dropped here does not open a hole, it just moves the error to a worse place with a worse
/// message. The one rule that exists *only* here is the reuse check.
/// </summary>
[TestFixture]
public class ChangePasswordCommandValidatorTests
{
    private const string Valid = "NewPass@123";

    private ChangePasswordCommandValidator _validator = null!;

    [SetUp]
    public void SetUp()
    {
        _validator = new ChangePasswordCommandValidator();
    }

    private static ChangePasswordCommand Command(string newPassword = Valid, string current = "OldPass@123") => new()
    {
        UserId = "user-1",
        CurrentPassword = current,
        NewPassword = newPassword
    };

    [Test]
    public async Task AConformingPassword_IsAccepted()
    {
        var result = await _validator.ValidateAsync(Command());

        Assert.That(result.IsValid, Is.True);
    }

    /// <summary>
    /// The cross-field rule, and the only one this validator owns outright — ASP.NET Identity's
    /// <c>PasswordOptions</c> has no concept of "different from the current one", so if this rule
    /// goes, a password change that changes nothing succeeds and reports success. That matters
    /// because <c>ChangePassword</c> also revokes every session: the user would be logged out
    /// everywhere and left on the same credential they were trying to rotate away from.
    /// </summary>
    [Test]
    public async Task ANewPasswordIdenticalToTheCurrentOne_IsRejected()
    {
        var result = await _validator.ValidateAsync(Command(newPassword: Valid, current: Valid));

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(e => e.PropertyName == nameof(ChangePasswordCommand.NewPassword)), Is.True);
    }

    [TestCase("Short@1", TestName = "too short")]
    [TestCase("nouppercase@123", TestName = "no uppercase")]
    [TestCase("NOLOWERCASE@123", TestName = "no lowercase")]
    [TestCase("NoDigits@abc", TestName = "no digit")]
    [TestCase("NoSpecial123abc", TestName = "no special character")]
    [TestCase("", TestName = "empty")]
    public async Task ANonConformingNewPassword_IsRejected(string newPassword)
    {
        var result = await _validator.ValidateAsync(Command(newPassword));

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(e => e.PropertyName == nameof(ChangePasswordCommand.NewPassword)), Is.True);
    }

    /// <summary>
    /// The upper bound is not cosmetic. Every candidate password is run through PBKDF2, so an
    /// unbounded field lets one request burn arbitrary CPU — and this endpoint is reachable by any
    /// authenticated user.
    /// </summary>
    [Test]
    public async Task AnAbsurdlyLongNewPassword_IsRejected()
    {
        var result = await _validator.ValidateAsync(Command("Aa1@" + new string('x', 100)));

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public async Task AMissingCurrentPassword_IsRejected()
    {
        var result = await _validator.ValidateAsync(Command(current: string.Empty));

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(e => e.PropertyName == nameof(ChangePasswordCommand.CurrentPassword)), Is.True);
    }

    [Test]
    public async Task AMissingUserId_IsRejected()
    {
        var command = Command() with { UserId = string.Empty };

        var result = await _validator.ValidateAsync(command);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(e => e.PropertyName == nameof(ChangePasswordCommand.UserId)), Is.True);
    }
}
