using EShop.Identity.Application.Auth.Commands.ConfirmEmail;
using EShop.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Identity.UnitTests.Auth;

[TestFixture]
public class ConfirmEmailCommandHandlerTests
{
    private Mock<UserManager<ApplicationUser>> _userManagerMock = null!;
    private Mock<ILogger<ConfirmEmailCommandHandler>> _loggerMock = null!;
    private ConfirmEmailCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _userManagerMock = MockUserManager();
        _loggerMock = new Mock<ILogger<ConfirmEmailCommandHandler>>();
        _handler = new ConfirmEmailCommandHandler(_userManagerMock.Object, _loggerMock.Object);
    }

    [Test]
    public async Task Handle_WithNonExistentUser_ReturnsFailure()
    {
        // Arrange
        var command = new ConfirmEmailCommand { UserId = "non-existent", Token = "token" };
        
        _userManagerMock.Setup(x => x.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Auth.UserNotFound"));
    }

    [Test]
    public async Task Handle_WithAlreadyConfirmedEmail_ReturnsSuccess()
    {
        // Arrange
        var command = new ConfirmEmailCommand { UserId = "1", Token = "token" };
        var user = new ApplicationUser { Id = "1", EmailConfirmed = true };
        
        _userManagerMock.Setup(x => x.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync(user);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Message, Does.Contain("already confirmed"));
    }

    [Test]
    public async Task Handle_WithInvalidToken_ReturnsFailure()
    {
        // Arrange
        var command = new ConfirmEmailCommand { UserId = "1", Token = "invalid-token" };
        var user = new ApplicationUser { Id = "1", EmailConfirmed = false };
        
        _userManagerMock.Setup(x => x.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.ConfirmEmailAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Invalid token" }));

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Auth.InvalidToken"));
    }

    [Test]
    public async Task Handle_WithValidToken_ReturnsSuccess()
    {
        // Arrange
        var command = new ConfirmEmailCommand { UserId = "1", Token = "valid-token" };
        var user = new ApplicationUser { Id = "1", EmailConfirmed = false };
        
        _userManagerMock.Setup(x => x.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.ConfirmEmailAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Success, Is.True);
    }

    private static Mock<UserManager<ApplicationUser>> MockUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }
}
