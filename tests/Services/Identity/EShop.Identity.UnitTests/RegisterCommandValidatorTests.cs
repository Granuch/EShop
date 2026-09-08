using EShop.Identity.Application.Auth.Commands.Register;

namespace EShop.Identity.UnitTests;

[TestFixture]
public class RegisterCommandValidatorTests
{
    private RegisterCommandValidator _validator = null!;

    [SetUp]
    public void SetUp()
    {
        _validator = new RegisterCommandValidator();
    }

    [Test]
    public async Task Should_HaveError_When_EmailIsEmpty()
    {
        var command = new RegisterCommand { Email = "", Password = "Test@1234", FirstName = "John", LastName = "Doe" };
        var result = await _validator.ValidateAsync(command);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(e => e.PropertyName == "Email"), Is.True);
    }

    [Test]
    public async Task Should_HaveError_When_EmailIsInvalid()
    {
        var command = new RegisterCommand { Email = "invalid-email", Password = "Test@1234", FirstName = "John", LastName = "Doe" };
        var result = await _validator.ValidateAsync(command);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(e => e.PropertyName == "Email"), Is.True);
    }

    [Test]
    public async Task Should_HaveError_When_PasswordIsTooShort()
    {
        var command = new RegisterCommand { Email = "test@test.com", Password = "Test@1", FirstName = "John", LastName = "Doe" };
        var result = await _validator.ValidateAsync(command);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(e => e.PropertyName == "Password"), Is.True);
    }

    [Test]
    public async Task Should_HaveError_When_PasswordMissingUppercase()
    {
        var command = new RegisterCommand { Email = "test@test.com", Password = "test@1234", FirstName = "John", LastName = "Doe" };
        var result = await _validator.ValidateAsync(command);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(e => e.PropertyName == "Password"), Is.True);
    }

    [Test]
    public async Task Should_HaveError_When_PasswordMissingSpecialChar()
    {
        var command = new RegisterCommand { Email = "test@test.com", Password = "TestTest1234", FirstName = "John", LastName = "Doe" };
        var result = await _validator.ValidateAsync(command);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(e => e.PropertyName == "Password"), Is.True);
    }

    [Test]
    public async Task Should_HaveError_When_FirstNameIsEmpty()
    {
        var command = new RegisterCommand { Email = "test@test.com", Password = "Test@1234", FirstName = "", LastName = "Doe" };
        var result = await _validator.ValidateAsync(command);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(e => e.PropertyName == "FirstName"), Is.True);
    }

    [Test]
    public async Task Should_HaveError_When_LastNameIsEmpty()
    {
        var command = new RegisterCommand { Email = "test@test.com", Password = "Test@1234", FirstName = "John", LastName = "" };
        var result = await _validator.ValidateAsync(command);
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Errors.Any(e => e.PropertyName == "LastName"), Is.True);
    }

    [Test]
    public async Task Should_NotHaveErrors_When_CommandIsValid()
    {
        var command = new RegisterCommand 
        { 
            Email = "test@test.com", 
            Password = "Test@1234", 
            FirstName = "John", 
            LastName = "Doe" 
        };
        var result = await _validator.ValidateAsync(command);
        Assert.That(result.IsValid, Is.True);
    }
}
