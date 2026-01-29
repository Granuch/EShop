using EShop.Identity.Application.Auth.Commands.RefreshToken;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Identity.UnitTests.Auth;

[TestFixture]
public class RefreshTokenCommandHandlerTests
{
    private Mock<ITokenService> _tokenServiceMock = null!;
    private Mock<UserManager<ApplicationUser>> _userManagerMock = null!;
    private Mock<ILogger<RefreshTokenCommandHandler>> _loggerMock = null!;
    private RefreshTokenCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _tokenServiceMock = new Mock<ITokenService>();
        _userManagerMock = MockUserManager();
        _loggerMock = new Mock<ILogger<RefreshTokenCommandHandler>>();
        _handler = new RefreshTokenCommandHandler(
            _tokenServiceMock.Object,
            _userManagerMock.Object,
            _loggerMock.Object);
    }

    [Test]
    public async Task Handle_WithEmptyToken_ReturnsFailure()
    {
        // Arrange
        var command = new RefreshTokenCommand { RefreshToken = "" };

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Auth.InvalidToken"));
    }

    [Test]
    public async Task Handle_WithInvalidToken_ReturnsFailure()
    {
        // Arrange
        var command = new RefreshTokenCommand { RefreshToken = "invalid-token" };
        
        _tokenServiceMock.Setup(x => x.ValidateRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, null, null));

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Auth.InvalidToken"));
    }

    [Test]
    public async Task Handle_WithDisabledUser_ReturnsFailure()
    {
        // Arrange
        var command = new RefreshTokenCommand { RefreshToken = "valid-token" };
        var user = new ApplicationUser { Id = "1", IsActive = false };
        var token = new RefreshTokenEntity { Token = "valid-token", UserId = "1" };

        _tokenServiceMock.Setup(x => x.ValidateRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, user, token));

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Auth.AccountDisabled"));
    }

    [Test]
    public async Task Handle_WithValidToken_ReturnsNewTokens()
    {
        // Arrange
        var command = new RefreshTokenCommand { RefreshToken = "valid-token", IpAddress = "127.0.0.1" };
        var user = new ApplicationUser { Id = "1", IsActive = true, IsDeleted = false, Email = "test@test.com" };
        var token = new RefreshTokenEntity { Token = "valid-token", UserId = "1" };

        _tokenServiceMock.Setup(x => x.ValidateRefreshTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, user, token));

        _tokenServiceMock.Setup(x => x.RotateRefreshTokenAsync(It.IsAny<RefreshTokenEntity>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-refresh-token");

        _tokenServiceMock.Setup(x => x.GenerateAccessTokenAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-access-token");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.AccessToken, Is.EqualTo("new-access-token"));
        Assert.That(result.Value.RefreshToken, Is.EqualTo("new-refresh-token"));
    }

    private static Mock<UserManager<ApplicationUser>> MockUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }
}
