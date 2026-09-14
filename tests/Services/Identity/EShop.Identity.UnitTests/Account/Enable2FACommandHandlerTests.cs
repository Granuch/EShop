using EShop.Identity.Application.Account.Commands.Enable2FA;
using EShop.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Identity.UnitTests.Account;

/// <summary>
/// TEST-06. <c>Enable2FA</c> is misleadingly named: it starts the enrolment, it does not arm
/// anything. <c>Verify2FA</c> is what calls <c>SetTwoFactorEnabledAsync(true)</c> once the user has
/// proved they can produce a code from the shared secret.
///
/// <para>
/// That distinction is not cosmetic — it is exactly what BUG-06 got wrong. The
/// <c>ICacheInvalidatingCommand</c> marker sat on this command, which changes no cached state, and
/// not on <c>Verify2FA</c>, which does, so a freshly-armed 2FA flag was served stale from the
/// profile cache. The first test below is the guard against re-merging the two.
/// </para>
/// </summary>
[TestFixture]
public class Enable2FACommandHandlerTests
{
    private const string UserId = "user-1";
    private const string Key = "ABCDEFGHIJKLMNOP";

    private Mock<UserManager<ApplicationUser>> _userManagerMock = null!;
    private Enable2FACommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _userManagerMock = MockUserManager();
        _handler = new Enable2FACommandHandler(
            _userManagerMock.Object,
            new Mock<ILogger<Enable2FACommandHandler>>().Object);
    }

    private static Enable2FACommand Command() => new() { UserId = UserId };

    private ApplicationUser ArrangeEnrollableUser()
    {
        var user = new ApplicationUser { Id = UserId, Email = "user@test.com" };
        _userManagerMock.Setup(x => x.FindByIdAsync(UserId)).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.GetAuthenticatorKeyAsync(user)).ReturnsAsync(Key);
        _userManagerMock.Setup(x => x.GetEmailAsync(user)).ReturnsAsync(user.Email);
        return user;
    }

    /// <summary>
    /// The BUG-06 guard. If this handler armed 2FA, a user would be locked out the moment they
    /// opened the setup page — they would be required to produce a code for a secret they had not
    /// yet scanned. Enrolment must stay a two-step flow.
    /// </summary>
    [Test]
    public async Task Handle_DoesNotArmTwoFactor_ThatIsVerify2FAsJob()
    {
        ArrangeEnrollableUser();

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        _userManagerMock.Verify(
            x => x.SetTwoFactorEnabledAsync(It.IsAny<ApplicationUser>(), It.IsAny<bool>()), Times.Never,
            "arming 2FA before the user has proved they can generate a code locks them out");
    }

    /// <summary>
    /// Re-enrolling someone who already has 2FA would issue a new secret and silently invalidate
    /// the authenticator app they are currently using — a self-inflicted lockout from a page a
    /// user could reach by simply revisiting it.
    /// </summary>
    [Test]
    public async Task Handle_WhenAlreadyEnabled_IssuesNoNewSecret()
    {
        var user = ArrangeEnrollableUser();
        user.TwoFactorEnabled = true;

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Account.2FAAlreadyEnabled"));
        _userManagerMock.Verify(
            x => x.ResetAuthenticatorKeyAsync(It.IsAny<ApplicationUser>()), Times.Never,
            "resetting the key here would break the user's working authenticator");
    }

    /// <summary>
    /// The QR URI is the only channel by which the shared secret reaches the user, so it has to
    /// carry the real key and identify the right account. The email is URL-encoded because an
    /// address with a '+' or '&amp;' would otherwise truncate the otpauth URI at that character.
    /// </summary>
    [Test]
    public async Task Handle_ReturnsAQrCodeUriCarryingTheSecretAndTheAccount()
    {
        var user = new ApplicationUser { Id = UserId, Email = "first+tag@test.com" };
        _userManagerMock.Setup(x => x.FindByIdAsync(UserId)).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.GetAuthenticatorKeyAsync(user)).ReturnsAsync(Key);
        _userManagerMock.Setup(x => x.GetEmailAsync(user)).ReturnsAsync(user.Email);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.QrCodeUri, Does.StartWith("otpauth://totp/EShop:"));
        Assert.That(result.Value.QrCodeUri, Does.Contain($"secret={Key}"));
        Assert.That(result.Value.QrCodeUri, Does.Not.Contain("+tag@"),
            "an unencoded '+' or '@' truncates the otpauth URI in most authenticator apps");
        Assert.That(result.Value.SharedKey.Replace(" ", string.Empty),
            Is.EqualTo(Key.ToLowerInvariant()),
            "the displayed key is the same secret, only regrouped for manual entry");
    }

    /// <summary>A user enrolling for the first time has no key yet, so one has to be minted.</summary>
    [Test]
    public async Task Handle_WithNoExistingKey_GeneratesOneBeforeBuildingTheUri()
    {
        var user = new ApplicationUser { Id = UserId, Email = "user@test.com" };
        _userManagerMock.Setup(x => x.FindByIdAsync(UserId)).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.GetEmailAsync(user)).ReturnsAsync(user.Email);

        var keyExists = false;
        _userManagerMock.Setup(x => x.GetAuthenticatorKeyAsync(user))
            .ReturnsAsync(() => keyExists ? Key : null);
        _userManagerMock.Setup(x => x.ResetAuthenticatorKeyAsync(user))
            .Callback(() => keyExists = true)
            .ReturnsAsync(IdentityResult.Success);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.QrCodeUri, Does.Contain($"secret={Key}"));
        _userManagerMock.Verify(x => x.ResetAuthenticatorKeyAsync(user), Times.Once);
    }

    /// <summary>
    /// If the key still cannot be read after a reset, the handler must fail rather than hand back
    /// a URI with an empty secret — which the authenticator app would accept, producing codes that
    /// never validate.
    /// </summary>
    [Test]
    public async Task Handle_WhenTheKeyCannotBeGenerated_FailsRatherThanReturningAnEmptySecret()
    {
        var user = new ApplicationUser { Id = UserId, Email = "user@test.com" };
        _userManagerMock.Setup(x => x.FindByIdAsync(UserId)).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.GetAuthenticatorKeyAsync(user)).ReturnsAsync((string?)null);
        _userManagerMock.Setup(x => x.ResetAuthenticatorKeyAsync(user)).ReturnsAsync(IdentityResult.Success);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Account.2FAError"));
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
