using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Messaging.Events;
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
    private Mock<IIntegrationEventOutbox> _outboxMock = null!;
    private Mock<ICurrentUserContext> _currentUserContextMock = null!;
    private Mock<ILogger<ConfirmEmailCommandHandler>> _loggerMock = null!;
    private ConfirmEmailCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _userManagerMock = MockUserManager();
        _outboxMock = new Mock<IIntegrationEventOutbox>();
        _currentUserContextMock = new Mock<ICurrentUserContext>();
        _currentUserContextMock.Setup(x => x.CorrelationId).Returns("test-correlation-id");
        _loggerMock = new Mock<ILogger<ConfirmEmailCommandHandler>>();
        _handler = new ConfirmEmailCommandHandler(
            _userManagerMock.Object,
            _outboxMock.Object,
            _currentUserContextMock.Object,
            _loggerMock.Object);
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

        // A rejected token must not publish the confirmation event.
        _outboxMock.Verify(o => o.Enqueue(
            It.IsAny<UserEmailConfirmedIntegrationEvent>(),
            It.IsAny<string?>()), Times.Never);
    }

    [Test]
    public async Task Handle_WithValidToken_ReturnsSuccess()
    {
        // Arrange
        var command = new ConfirmEmailCommand { UserId = "1", Token = "valid-token" };
        var user = new ApplicationUser { Id = "1", Email = "test@test.com", EmailConfirmed = false };

        _userManagerMock.Setup(x => x.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync(user);

        _userManagerMock.Setup(x => x.ConfirmEmailAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Success, Is.True);

        // Verify the integration event was enqueued.
        _outboxMock.Verify(o => o.Enqueue(
            It.IsAny<UserEmailConfirmedIntegrationEvent>(),
            It.IsAny<string?>()), Times.Once);
    }

    [Test]
    public void Handler_DoesNotDriveTheUnitOfWork()
    {
        // ConfirmEmailCommand is ITransactionalCommand, so TransactionBehavior owns the
        // transaction. This handler previously opened and committed its own inside the
        // behavior's, which closed the transaction the behavior still believed it owned and left
        // the behavior's commit/rollback operating on nothing. Taking no IUnitOfWork at all is
        // what makes that structurally impossible, so pin the constructor shape.
        var takesUnitOfWork = typeof(ConfirmEmailCommandHandler)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Any(p => p.ParameterType == typeof(IUnitOfWork));

        Assert.That(takesUnitOfWork, Is.False,
            "ConfirmEmailCommandHandler must not depend on IUnitOfWork; TransactionBehavior owns the transaction.");
    }

    private static Mock<UserManager<ApplicationUser>> MockUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }
}
