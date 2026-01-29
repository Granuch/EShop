using EShop.Identity.Application.Auth.Commands.RevokeToken;
using EShop.Identity.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Identity.UnitTests.Auth;

[TestFixture]
public class RevokeTokenCommandHandlerTests
{
    private Mock<ITokenService> _tokenServiceMock = null!;
    private Mock<ILogger<RevokeTokenCommandHandler>> _loggerMock = null!;
    private RevokeTokenCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _tokenServiceMock = new Mock<ITokenService>();
        _loggerMock = new Mock<ILogger<RevokeTokenCommandHandler>>();
        _handler = new RevokeTokenCommandHandler(_tokenServiceMock.Object, _loggerMock.Object);
    }

    [Test]
    public async Task Handle_WithEmptyToken_ReturnsFailure()
    {
        // Arrange
        var command = new RevokeTokenCommand { RefreshToken = "" };

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
        var command = new RevokeTokenCommand { RefreshToken = "valid-token", IpAddress = "127.0.0.1" };

        _tokenServiceMock.Setup(x => x.RevokeTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        _tokenServiceMock.Verify(x => x.RevokeTokenAsync("valid-token", "127.0.0.1", It.IsAny<CancellationToken>()), Times.Once);
    }
}
