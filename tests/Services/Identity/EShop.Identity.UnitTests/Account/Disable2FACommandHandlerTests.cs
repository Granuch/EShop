using EShop.Identity.Application.Account.Commands.Disable2FA;
using EShop.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Identity.UnitTests.Account;

/// <summary>
/// TEST-06. Disabling 2FA is the one operation in the flow that removes a security control, so
/// the ordering inside the handler is the whole point: the authenticator code is verified
/// <i>before</i> <c>SetTwoFactorEnabledAsync(false)</c> runs.
///
/// <para>
/// If those two steps were ever reversed — or the verification result ignored — anyone holding a
/// stolen access token could strip the second factor off an account without possessing the second
/// factor, which is precisely the attack 2FA exists to stop. Nothing about the code's shape makes
/// that ordering obvious, and a refactor that hoists the "disable" call is not visibly wrong.
/// </para>
/// </summary>
[TestFixture]
public class Disable2FACommandHandlerTests
{
    private const string UserId = "user-1";
    private const string ValidCode = "123456";

    private Mock<UserManager<ApplicationUser>> _userManagerMock = null!;
    private Disable2FACommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _userManagerMock = MockUserManager();
        _handler = new Disable2FACommandHandler(
            _userManagerMock.Object,
            new Mock<ILogger<Disable2FACommandHandler>>().Object);
    }

    private static Disable2FACommand Command(string code = ValidCode) => new()
    {
        UserId = UserId,
        Code = code
    };

    private ApplicationUser ArrangeUserWith2FA()
    {
        var user = new ApplicationUser { Id = UserId, Email = "user@test.com", TwoFactorEnabled = true };
        _userManagerMock.Setup(x => x.FindByIdAsync(UserId)).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.VerifyTwoFactorTokenAsync(user, It.IsAny<string>(), ValidCode))
            .ReturnsAsync(true);
        _userManagerMock.Setup(x => x.SetTwoFactorEnabledAsync(user, false))
            .ReturnsAsync(IdentityResult.Success);
        _userManagerMock.Setup(x => x.ResetAuthenticatorKeyAsync(user))
            .ReturnsAsync(IdentityResult.Success);
        return user;
    }

    /// <summary>The property the whole handler exists to uphold.</summary>
    [Test]
    public async Task Handle_WithAnInvalidCode_LeavesTwoFactorEnabled()
    {
        var user = ArrangeUserWith2FA();
        _userManagerMock.Setup(x => x.VerifyTwoFactorTokenAsync(user, It.IsAny<string>(), "000000"))
            .ReturnsAsync(false);

        var result = await _handler.Handle(Command("000000"), CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Account.InvalidCode"));
        _userManagerMock.Verify(
            x => x.SetTwoFactorEnabledAsync(It.IsAny<ApplicationUser>(), false), Times.Never,
            "a stolen access token must not be enough to strip the second factor");
        _userManagerMock.Verify(
            x => x.ResetAuthenticatorKeyAsync(It.IsAny<ApplicationUser>()), Times.Never,
            "nor to invalidate the secret the legitimate owner is still using");
    }

    /// <summary>
    /// The key must be reset on the way out. Leaving it in place means a secret that may already
    /// have leaked — a screenshotted QR code, a shared setup link — still produces valid codes the
    /// next time the user turns 2FA back on, so re-enrolling would not actually re-secure anything.
    /// </summary>
    [Test]
    public async Task Handle_OnSuccess_DisablesAndThenDiscardsTheOldSecret()
    {
        var user = ArrangeUserWith2FA();

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Success, Is.True);
        _userManagerMock.Verify(x => x.SetTwoFactorEnabledAsync(user, false), Times.Once);
        _userManagerMock.Verify(x => x.ResetAuthenticatorKeyAsync(user), Times.Once,
            "re-enabling later must issue a fresh secret, not resurrect the discarded one");
    }

    /// <summary>
    /// If the store refuses the change, the handler must report that rather than continuing to the
    /// key reset — which would leave 2FA on with a secret the user's app no longer knows, i.e. a
    /// permanent lockout produced by a failure path.
    /// </summary>
    [Test]
    public async Task Handle_WhenTheStoreRejectsTheChange_DoesNotDiscardTheSecret()
    {
        var user = ArrangeUserWith2FA();
        _userManagerMock.Setup(x => x.SetTwoFactorEnabledAsync(user, false))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "store rejected it" }));

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Account.2FAError"));
        _userManagerMock.Verify(
            x => x.ResetAuthenticatorKeyAsync(It.IsAny<ApplicationUser>()), Times.Never,
            "2FA is still on, so destroying the secret here would lock the user out permanently");
    }

    /// <summary>
    /// Idempotency guard. A repeated disable must be a no-op, not a key reset — otherwise a
    /// double-submitted form churns the secret of an account that has no 2FA to churn.
    /// </summary>
    [Test]
    public async Task Handle_WhenTwoFactorIsNotEnabled_ChangesNothing()
    {
        var user = new ApplicationUser { Id = UserId, TwoFactorEnabled = false };
        _userManagerMock.Setup(x => x.FindByIdAsync(UserId)).ReturnsAsync(user);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Account.2FANotEnabled"));
        _userManagerMock.Verify(
            x => x.VerifyTwoFactorTokenAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
        _userManagerMock.Verify(
            x => x.ResetAuthenticatorKeyAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Test]
    public async Task Handle_WithNonExistentUser_ReturnsNotFound()
    {
        _userManagerMock.Setup(x => x.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Account.UserNotFound"));
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
