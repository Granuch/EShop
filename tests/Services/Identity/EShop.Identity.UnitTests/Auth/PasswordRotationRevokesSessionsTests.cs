using EShop.BuildingBlocks.Domain;
using EShop.Identity.Application.Account.Commands.ChangePassword;
using EShop.Identity.Application.Auth.Commands.ResetPassword;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Moq;

namespace EShop.Identity.UnitTests.Auth;

/// <summary>
/// SEC-06. Both password-rotation handlers used to wrap the "revoke every refresh token" step
/// in a try/catch that logged and continued, so a failed revoke produced a 200 while every
/// pre-existing session stayed valid — the exact outcome rotating a password is meant to
/// prevent, and invisible to the caller.
///
/// The handlers are ITransactionalCommand, so the correct shape is to let the revoke throw:
/// TransactionBehavior rolls the password change back and the caller sees the failure. These
/// tests pin that the exception is no longer swallowed.
///
/// Note the rollback itself is not asserted here and cannot be asserted by the integration
/// suite either — it runs on EF InMemory, where BeginTransaction/Rollback are no-ops. Stage 8's
/// Postgres-backed suite is what will prove the rollback rather than just the propagation.
/// </summary>
[TestFixture]
public class PasswordRotationRevokesSessionsTests
{
    private const string UserId = "user-1";

    private Mock<UserManager<ApplicationUser>> _userManagerMock = null!;
    private Mock<IRefreshTokenRepository> _refreshTokenRepositoryMock = null!;
    private ApplicationUser _user = null!;

    [SetUp]
    public void SetUp()
    {
        _userManagerMock = MockUserManager();
        _refreshTokenRepositoryMock = new Mock<IRefreshTokenRepository>();

        _user = new ApplicationUser
        {
            Id = UserId,
            Email = "user@test.com"
        };

        _userManagerMock.Setup(x => x.FindByIdAsync(UserId)).ReturnsAsync(_user);
    }

    [Test]
    public void ChangePassword_WhenRevokeFails_ShouldNotReportSuccess()
    {
        _userManagerMock
            .Setup(x => x.ChangePasswordAsync(_user, "OldPass1!", "NewPass1!"))
            .ReturnsAsync(IdentityResult.Success);
        _refreshTokenRepositoryMock
            .Setup(x => x.RevokeAllUserTokensAsync(UserId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("revoke failed"));

        var handler = new ChangePasswordCommandHandler(
            _userManagerMock.Object,
            _refreshTokenRepositoryMock.Object,
            Mock.Of<ILogger<ChangePasswordCommandHandler>>());

        Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new ChangePasswordCommand
            {
                UserId = UserId,
                CurrentPassword = "OldPass1!",
                NewPassword = "NewPass1!"
            },
            CancellationToken.None));
    }

    [Test]
    public async Task ChangePassword_OnSuccess_ShouldRevokeEverySession()
    {
        _userManagerMock
            .Setup(x => x.ChangePasswordAsync(_user, "OldPass1!", "NewPass1!"))
            .ReturnsAsync(IdentityResult.Success);

        var handler = new ChangePasswordCommandHandler(
            _userManagerMock.Object,
            _refreshTokenRepositoryMock.Object,
            Mock.Of<ILogger<ChangePasswordCommandHandler>>());

        var result = await handler.Handle(
            new ChangePasswordCommand
            {
                UserId = UserId,
                CurrentPassword = "OldPass1!",
                NewPassword = "NewPass1!"
            },
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        _refreshTokenRepositoryMock.Verify(
            x => x.RevokeAllUserTokensAsync(UserId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public void ResetPassword_WhenRevokeFails_ShouldNotReportSuccess()
    {
        _userManagerMock
            .Setup(x => x.ResetPasswordAsync(_user, "reset-token", "NewPass1!"))
            .ReturnsAsync(IdentityResult.Success);
        _refreshTokenRepositoryMock
            .Setup(x => x.RevokeAllUserTokensAsync(UserId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("revoke failed"));

        var handler = new ResetPasswordCommandHandler(
            _userManagerMock.Object,
            _refreshTokenRepositoryMock.Object,
            Mock.Of<ILogger<ResetPasswordCommandHandler>>());

        Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new ResetPasswordCommand
            {
                UserId = UserId,
                Token = "reset-token",
                NewPassword = "NewPass1!"
            },
            CancellationToken.None));
    }

    [Test]
    public async Task ResetPassword_OnSuccess_ShouldRevokeEverySession()
    {
        _userManagerMock
            .Setup(x => x.ResetPasswordAsync(_user, "reset-token", "NewPass1!"))
            .ReturnsAsync(IdentityResult.Success);

        var handler = new ResetPasswordCommandHandler(
            _userManagerMock.Object,
            _refreshTokenRepositoryMock.Object,
            Mock.Of<ILogger<ResetPasswordCommandHandler>>());

        var result = await handler.Handle(
            new ResetPasswordCommand
            {
                UserId = UserId,
                Token = "reset-token",
                NewPassword = "NewPass1!"
            },
            CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        _refreshTokenRepositoryMock.Verify(
            x => x.RevokeAllUserTokensAsync(UserId, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// The swallow used to be paired with a hand-rolled _unitOfWork.SaveChangesAsync. Both
    /// halves had to go: a handler that drives the unit of work itself while marked
    /// ITransactionalCommand fights the behavior that owns the transaction (the same defect
    /// BUG-07 fixed in ConfirmEmailCommandHandler). This guards the constructor shape so the
    /// dependency cannot quietly come back.
    /// </summary>
    [Test]
    public void Handlers_DoNotDriveTheUnitOfWork()
    {
        foreach (var handlerType in new[] { typeof(ChangePasswordCommandHandler), typeof(ResetPasswordCommandHandler) })
        {
            var takesUnitOfWork = handlerType
                .GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Any(p => p.ParameterType == typeof(IUnitOfWork));

            Assert.That(takesUnitOfWork, Is.False,
                $"{handlerType.Name} must let TransactionBehavior own the unit of work.");
        }
    }

    private static Mock<UserManager<ApplicationUser>> MockUserManager()
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);
    }
}
