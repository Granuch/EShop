using EShop.Identity.Application.Account.Commands.Verify2FA;
using EShop.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Identity.UnitTests.Account;

[TestFixture]
public class Verify2FACommandHandlerTests
{
    private Mock<UserManager<ApplicationUser>> _userManagerMock = null!;
    private Mock<ILogger<Verify2FACommandHandler>> _loggerMock = null!;
    private Verify2FACommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _userManagerMock = MockUserManager();
        _loggerMock = new Mock<ILogger<Verify2FACommandHandler>>();
        _handler = new Verify2FACommandHandler(_userManagerMock.Object, _loggerMock.Object);
    }

    [Test]
    public async Task Handle_WithNonExistentUser_ReturnsFailure()
    {
        // Arrange
        var command = new Verify2FACommand { UserId = "non-existent", Code = "123456" };
        
        _userManagerMock.Setup(x => x.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Account.UserNotFound"));
    }

    [Test]
    public async Task Handle_WithInvalidCode_ReturnsFailure()
    {
        // Arrange
        var command = new Verify2FACommand { UserId = "1", Code = "invalid" };
        var user = new ApplicationUser { Id = "1" };
        
        _userManagerMock.Setup(x => x.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.VerifyTwoFactorTokenAsync(
            It.IsAny<ApplicationUser>(), 
            It.IsAny<string>(), 
            It.IsAny<string>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Account.InvalidCode"));
    }

    [Test]
    public async Task Handle_WithValidCode_ReturnsSuccessWithRecoveryCodes()
    {
        // Arrange
        var command = new Verify2FACommand { UserId = "1", Code = "123456" };
        var user = new ApplicationUser { Id = "1" };
        var recoveryCodes = new[] { "code1", "code2", "code3" };
        
        _userManagerMock.Setup(x => x.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.VerifyTwoFactorTokenAsync(
            It.IsAny<ApplicationUser>(), 
            It.IsAny<string>(), 
            It.IsAny<string>()))
            .ReturnsAsync(true);

        _userManagerMock.Setup(x => x.SetTwoFactorEnabledAsync(It.IsAny<ApplicationUser>(), true))
            .ReturnsAsync(IdentityResult.Success);

        _userManagerMock.Setup(x => x.GenerateNewTwoFactorRecoveryCodesAsync(It.IsAny<ApplicationUser>(), 10))
            .ReturnsAsync(recoveryCodes);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Success, Is.True);
        Assert.That(result.Value.RecoveryCodes, Is.Not.Empty);
    }

    private static Mock<UserManager<ApplicationUser>> MockUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var options = new IdentityOptions
        {
            Tokens = new TokenOptions
            {
                AuthenticatorTokenProvider = "Authenticator"
            }
        };
        var optionsAccessor = new Mock<IOptions<IdentityOptions>>();
        optionsAccessor.Setup(x => x.Value).Returns(options);
        
        return new Mock<UserManager<ApplicationUser>>(
            store.Object, 
            optionsAccessor.Object, 
            null!, null!, null!, null!, null!, null!, null!);
    }
}
