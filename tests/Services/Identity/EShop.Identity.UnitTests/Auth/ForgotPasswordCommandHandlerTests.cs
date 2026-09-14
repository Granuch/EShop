using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Domain;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Identity.Application.Auth.Commands.ForgotPassword;
using EShop.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Identity.UnitTests.Auth;

[TestFixture]
public class ForgotPasswordCommandHandlerTests
{
    private Mock<UserManager<ApplicationUser>> _userManagerMock = null!;
    private Mock<IIntegrationEventOutbox> _outboxMock = null!;
    private Mock<ICurrentUserContext> _currentUserContextMock = null!;
    private ForgotPasswordCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _userManagerMock = MockUserManager();
        _outboxMock = new Mock<IIntegrationEventOutbox>();
        _currentUserContextMock = new Mock<ICurrentUserContext>();
        _currentUserContextMock.Setup(x => x.CorrelationId).Returns("test-correlation-id");

        _handler = new ForgotPasswordCommandHandler(
            _userManagerMock.Object,
            _outboxMock.Object,
            _currentUserContextMock.Object,
            Mock.Of<ILogger<ForgotPasswordCommandHandler>>());
    }

    [Test]
    public async Task Handle_WhenUserNotFound_ShouldReturnSuccessWithoutPublishingEvent()
    {
        _userManagerMock
            .Setup(x => x.FindByEmailAsync("missing@test.com"))
            .ReturnsAsync((ApplicationUser?)null);

        var result = await _handler.Handle(new ForgotPasswordCommand { Email = "missing@test.com" }, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        _outboxMock.Verify(x => x.Enqueue(It.IsAny<PasswordResetRequestedIntegrationEvent>(), It.IsAny<string?>()), Times.Never);
        // The handler must not drive the unit of work at all now — TransactionBehavior owns it,
        // and an inner commit would close the transaction the behavior still believes it holds
        // (the BUG-07 defect). The dependency is gone from the constructor entirely; this asserts
        // the shape so it cannot come back.
        var takesUnitOfWork = typeof(ForgotPasswordCommandHandler)
            .GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Any(p => p.ParameterType == typeof(IUnitOfWork));
        Assert.That(takesUnitOfWork, Is.False);
    }

    [Test]
    public async Task Handle_WhenUserExists_ShouldPublishPasswordResetRequestedEvent()
    {
        var user = new ApplicationUser
        {
            Id = "user-1",
            Email = "user@test.com"
        };

        _userManagerMock
            .Setup(x => x.FindByEmailAsync("user@test.com"))
            .ReturnsAsync(user);
        _userManagerMock
            .Setup(x => x.GeneratePasswordResetTokenAsync(user))
            .ReturnsAsync("reset-token");

        var result = await _handler.Handle(new ForgotPasswordCommand { Email = "user@test.com" }, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        _outboxMock.Verify(x => x.Enqueue(
            It.Is<PasswordResetRequestedIntegrationEvent>(e =>
                e.UserId == "user-1"
                && e.ResetToken == "reset-token"
                && e.CorrelationId == "test-correlation-id"),
            "test-correlation-id"), Times.Once);
        // No SaveChangesAsync assertion: persistence is TransactionBehavior's job now. What this
        // handler owes is the outbox enqueue, asserted above.
    }

    private static Mock<UserManager<ApplicationUser>> MockUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }
}
