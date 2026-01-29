using EShop.Identity.Application.Auth.Commands.ResetPassword;

namespace EShop.Identity.UnitTests.Auth;

[TestFixture]
public class ResetPasswordCommandValidatorTests
{
    private ResetPasswordCommandValidator _validator = null!;

    [SetUp]
    public void SetUp()
    {
        _validator = new ResetPasswordCommandValidator();
    }

    [Test]
    public async Task Should_HaveError_When_UserIdIsEmpty()
    {
        var command = new ResetPasswordCommand { UserId = "", Token = "token", NewPassword = "Test@1234" };
        var result = await _validator.ValidateAsync(command);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(e => e.PropertyName == "UserId"), Is.True);
    }

    [Test]
    public async Task Should_HaveError_When_TokenIsEmpty()
    {
        var command = new ResetPasswordCommand { UserId = "1", Token = "", NewPassword = "Test@1234" };
        var result = await _validator.ValidateAsync(command);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(e => e.PropertyName == "Token"), Is.True);
    }

    [Test]
    public async Task Should_HaveError_When_PasswordIsTooShort()
    {
        var command = new ResetPasswordCommand { UserId = "1", Token = "token", NewPassword = "Test@1" };
        var result = await _validator.ValidateAsync(command);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(e => e.PropertyName == "NewPassword"), Is.True);
    }

    [Test]
    public async Task Should_HaveError_When_PasswordMissingUppercase()
    {
        var command = new ResetPasswordCommand { UserId = "1", Token = "token", NewPassword = "test@1234" };
        var result = await _validator.ValidateAsync(command);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(e => e.PropertyName == "NewPassword"), Is.True);
    }

    [Test]
    public async Task Should_HaveError_When_PasswordMissingSpecialChar()
    {
        var command = new ResetPasswordCommand { UserId = "1", Token = "token", NewPassword = "TestTest1234" };
        var result = await _validator.ValidateAsync(command);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(e => e.PropertyName == "NewPassword"), Is.True);
    }

    [Test]
    public async Task Should_NotHaveErrors_When_CommandIsValid()
    {
        var command = new ResetPasswordCommand 
        { 
            UserId = "1", 
            Token = "valid-token", 
            NewPassword = "Test@1234" 
        };
        var result = await _validator.ValidateAsync(command);
        Assert.That(result.IsValid, Is.True);
    }
}
