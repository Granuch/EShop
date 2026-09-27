using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Identity.Application.Auth.Commands.ResetPassword;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Identity.UnitTests.Auth;

/// <summary>
/// A redeemed reset token proves the caller reads the account's mailbox, so a successful reset
/// confirms an unconfirmed address. That is what lets an administrator's invitee — whose only email
/// is the reset link — sign in once <c>SignIn.RequireConfirmedEmail</c> is on.
///
/// <para>
/// The failure case is the load-bearing one: <c>TransactionBehavior</c> commits on a failure
/// <c>Result</c>, so a flag set on the tracked user before the token was checked would be persisted
/// by a request whose token was wrong.
/// </para>
/// </summary>
[TestFixture]
public class ResetPasswordConfirmsEmailTests
{
    private const string UserId = "user-1";

    private Mock<UserManager<ApplicationUser>> _userManagerMock = null!;
    private Mock<IIntegrationEventOutbox> _outboxMock = null!;
    private ResetPasswordCommandHandler _handler = null!;
    private ApplicationUser _user = null!;

    [SetUp]
    public void SetUp()
    {
        _userManagerMock = MockUserManager();
        _outboxMock = new Mock<IIntegrationEventOutbox>();
        _user = new ApplicationUser { Id = UserId, Email = "invitee@test.com", EmailConfirmed = false };
        _userManagerMock.Setup(x => x.FindByIdAsync(UserId)).ReturnsAsync(_user);

        _handler = new ResetPasswordCommandHandler(
            _userManagerMock.Object,
            Mock.Of<IRefreshTokenRepository>(),
            _outboxMock.Object,
            Mock.Of<ICurrentUserContext>(),
            Mock.Of<ILogger<ResetPasswordCommandHandler>>());
    }

    private static ResetPasswordCommand Command() => new()
    {
        UserId = UserId,
        Token = "reset-token",
        NewPassword = "NewPass1!"
    };

    [Test]
    public async Task ASuccessfulReset_ConfirmsAnUnconfirmedAddress_AndAnnouncesIt()
    {
        _userManagerMock.Setup(x => x.ResetPasswordAsync(_user, "reset-token", "NewPass1!"))
            .ReturnsAsync(IdentityResult.Success);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(_user.EmailConfirmed, Is.True);
        _outboxMock.Verify(x => x.Enqueue(
            It.Is<UserEmailConfirmedIntegrationEvent>(e => e.UserId == UserId), It.IsAny<string?>()), Times.Once);
    }

    [Test]
    public async Task AFailedReset_LeavesTheAddressUnconfirmed_AndAnnouncesNothing()
    {
        _userManagerMock.Setup(x => x.ResetPasswordAsync(_user, "reset-token", "NewPass1!"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Invalid token." }));

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(_user.EmailConfirmed, Is.False,
            "TransactionBehavior commits a failure Result, so a flag set here would be persisted");
        _outboxMock.Verify(x => x.Enqueue(It.IsAny<IIntegrationEvent>(), It.IsAny<string?>()), Times.Never);
    }

    [Test]
    public async Task AResetOnAnAlreadyConfirmedAddress_AnnouncesNothing()
    {
        _user.EmailConfirmed = true;
        _userManagerMock.Setup(x => x.ResetPasswordAsync(_user, "reset-token", "NewPass1!"))
            .ReturnsAsync(IdentityResult.Success);

        await _handler.Handle(Command(), CancellationToken.None);

        _outboxMock.Verify(x => x.Enqueue(It.IsAny<IIntegrationEvent>(), It.IsAny<string?>()), Times.Never);
    }

    private static Mock<UserManager<ApplicationUser>> MockUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var optionsAccessor = new Mock<IOptions<IdentityOptions>>();
        optionsAccessor.Setup(x => x.Value).Returns(new IdentityOptions());

        return new Mock<UserManager<ApplicationUser>>(
            store.Object,
            optionsAccessor.Object,
            null!, null!, null!, null!, null!, null!, null!);
    }
}
