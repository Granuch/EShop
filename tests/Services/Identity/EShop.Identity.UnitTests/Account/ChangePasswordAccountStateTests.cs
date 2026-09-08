using EShop.Identity.Application.Account.Commands.ChangePassword;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Identity.UnitTests.Account;

/// <summary>
/// TEST-06, guarding BUG-10.
///
/// <para>
/// The two password-mutation paths used to disagree about account state: <c>ResetPassword</c>
/// refused disabled and deleted accounts, <c>ChangePassword</c> checked only for a missing user.
/// So an account that had been deactivated could still have its password changed by anyone
/// holding a token issued before the deactivation — the deactivation did not actually close the
/// door it appeared to close.
/// </para>
///
/// <para>
/// The parity is easy to lose again because the two handlers are edited independently and the
/// check is three lines in one of them. These tests pin both halves: the refusal itself, and that
/// the refusal happens <i>before</i> any password work is attempted.
/// </para>
/// </summary>
[TestFixture]
public class ChangePasswordAccountStateTests
{
    private Mock<UserManager<ApplicationUser>> _userManagerMock = null!;
    private Mock<IRefreshTokenRepository> _refreshTokenRepositoryMock = null!;
    private ChangePasswordCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _userManagerMock = MockUserManager();
        _refreshTokenRepositoryMock = new Mock<IRefreshTokenRepository>();
        _handler = new ChangePasswordCommandHandler(
            _userManagerMock.Object,
            _refreshTokenRepositoryMock.Object,
            new Mock<ILogger<ChangePasswordCommandHandler>>().Object);
    }

    private static ChangePasswordCommand Command() => new()
    {
        UserId = "user-1",
        CurrentPassword = "Current@123456",
        NewPassword = "New@123456"
    };

    [Test]
    public async Task Handle_OnADeactivatedAccount_IsRefusedWithAccountDisabled()
    {
        var user = new ApplicationUser { Id = "user-1", Email = "user@test.com" };
        user.Deactivate();
        _userManagerMock.Setup(x => x.FindByIdAsync("user-1")).ReturnsAsync(user);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Auth.AccountDisabled"),
            "this must match ResetPasswordCommandHandler's answer for the same account state");
    }

    /// <summary>
    /// The refusal has to come before the password work, not after. If the handler reached
    /// <c>ChangePasswordAsync</c> first, a deactivated account's password would already have been
    /// rotated by the time the check ran.
    /// </summary>
    [Test]
    public async Task Handle_OnADeactivatedAccount_NeverAttemptsThePasswordChange()
    {
        var user = new ApplicationUser { Id = "user-1", Email = "user@test.com" };
        user.Deactivate();
        _userManagerMock.Setup(x => x.FindByIdAsync("user-1")).ReturnsAsync(user);

        await _handler.Handle(Command(), CancellationToken.None);

        _userManagerMock.Verify(
            x => x.ChangePasswordAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
        _refreshTokenRepositoryMock.Verify(
            x => x.RevokeAllUserTokensAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// A soft-deleted user sets IsActive false as well, so it takes the same path even where the
    /// DbContext's global <c>!IsDeleted</c> filter has not already hidden the row.
    /// </summary>
    [Test]
    public async Task Handle_OnASoftDeletedAccount_IsRefused()
    {
        var user = new ApplicationUser { Id = "user-1", Email = "user@test.com" };
        user.SoftDelete();
        _userManagerMock.Setup(x => x.FindByIdAsync("user-1")).ReturnsAsync(user);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Auth.AccountDisabled"));
    }

    [Test]
    public async Task Handle_OnAnActiveAccount_ProceedsWithTheChange()
    {
        var user = new ApplicationUser { Id = "user-1", Email = "user@test.com" };
        _userManagerMock.Setup(x => x.FindByIdAsync("user-1")).ReturnsAsync(user);
        _userManagerMock
            .Setup(x => x.ChangePasswordAsync(user, "Current@123456", "New@123456"))
            .ReturnsAsync(IdentityResult.Success);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        _userManagerMock.Verify(
            x => x.ChangePasswordAsync(user, "Current@123456", "New@123456"), Times.Once);
    }

    [Test]
    public async Task Handle_ForAMissingUser_ReturnsNotFound()
    {
        _userManagerMock.Setup(x => x.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Account.NotFound"));
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
