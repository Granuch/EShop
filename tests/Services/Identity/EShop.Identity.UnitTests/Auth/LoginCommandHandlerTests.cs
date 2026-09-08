using EShop.Identity.Application.Auth.Commands.Login;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using EShop.Identity.Domain.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Identity.UnitTests.Auth;

/// <summary>
/// TEST-06. <c>LoginCommandHandler</c> is the largest handler in the service and had no unit test
/// at all; the integration suite exercises it end to end but cannot reach the properties below,
/// because they are about what the handler <i>refrains</i> from doing.
///
/// <para>
/// Three things are pinned here, all of them security properties that fail silently if broken:
/// (1) every credential-rejection path returns the byte-identical <c>Auth.InvalidCredentials</c>
/// answer, so the response cannot be used to enumerate accounts; (2) the brute-force gate runs
/// before any credential work, so a blocked caller cannot keep probing; (3) the 2FA-required
/// branch issues no tokens.
/// </para>
/// </summary>
[TestFixture]
public class LoginCommandHandlerTests
{
    private const string Email = "user@test.com";
    private const string Password = "Correct@123456";
    private const string Ip = "203.0.113.7";

    private Mock<UserManager<ApplicationUser>> _userManagerMock = null!;
    private Mock<SignInManager<ApplicationUser>> _signInManagerMock = null!;
    private Mock<ITokenService> _tokenServiceMock = null!;
    private Mock<ILoginAttemptTracker> _trackerMock = null!;
    private Mock<IUserRepository> _userRepositoryMock = null!;
    private LoginCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _userManagerMock = MockUserManager();
        _signInManagerMock = MockSignInManager(_userManagerMock);
        _tokenServiceMock = new Mock<ITokenService>();
        _trackerMock = new Mock<ILoginAttemptTracker>();
        _userRepositoryMock = new Mock<IUserRepository>();

        // Default: the brute-force gate lets the attempt through, so each test only has to arrange
        // the state it is actually about.
        _trackerMock
            .Setup(x => x.ValidateAttemptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginAttemptValidationResult.Allowed());

        _handler = new LoginCommandHandler(
            _userManagerMock.Object,
            _signInManagerMock.Object,
            _tokenServiceMock.Object,
            _trackerMock.Object,
            _userRepositoryMock.Object,
            new Mock<ILogger<LoginCommandHandler>>().Object);
    }

    private static LoginCommand Command(string? twoFactorCode = null) => new()
    {
        Email = Email,
        Password = Password,
        TwoFactorCode = twoFactorCode,
        IpAddress = Ip
    };

    /// <summary>
    /// Arranges a user the handler will accept, so each failure test can spoil exactly one thing.
    /// </summary>
    private ApplicationUser ArrangeSignInReadyUser()
    {
        var user = new ApplicationUser
        {
            Id = "user-1",
            Email = Email,
            FirstName = "Test",
            LastName = "User"
        };

        _userManagerMock.Setup(x => x.FindByEmailAsync(Email)).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.CheckPasswordAsync(user, Password)).ReturnsAsync(true);
        _userManagerMock.Setup(x => x.IsLockedOutAsync(user)).ReturnsAsync(false);
        _userManagerMock.Setup(x => x.GetRolesAsync(user)).ReturnsAsync(new List<string> { "Customer" });
        _signInManagerMock.Setup(x => x.CanSignInAsync(user)).ReturnsAsync(true);

        _tokenServiceMock.Setup(x => x.GenerateAccessTokenAsync(user, It.IsAny<CancellationToken>()))
            .ReturnsAsync("access-token");
        _tokenServiceMock.Setup(x => x.GenerateRefreshTokenAsync(user.Id, Ip, It.IsAny<CancellationToken>()))
            .ReturnsAsync("refresh-token");

        return user;
    }

    // ---------------------------------------------------------------------------------------
    // (1) Enumeration uniformity
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Five different causes, one indistinguishable answer. The handler knows precisely why each
    /// of these failed and deliberately does not say — a caller who could tell "no such user" from
    /// "wrong password" can enumerate the user table, and one who could spot "account disabled"
    /// learns that the address is registered. The literal message is asserted, not just the code,
    /// because the enumeration channel is whatever reaches the client.
    /// </summary>
    [TestCase("no_such_user")]
    [TestCase("deactivated")]
    [TestCase("locked_out")]
    [TestCase("cannot_sign_in")]
    [TestCase("wrong_password")]
    public async Task Handle_ForEveryCredentialRejection_ReturnsTheSameOpaqueAnswer(string cause)
    {
        ArrangeCause(cause);

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsFailure, Is.True, $"'{cause}' must not authenticate");
        Assert.That(result.Error!.Code, Is.EqualTo("Auth.InvalidCredentials"),
            "a rejection reason must never be inferable from the error code");
        Assert.That(result.Error.Message, Is.EqualTo("Invalid email or password"),
            "a rejection reason must never be inferable from the message either");
    }

    private void ArrangeCause(string cause)
    {
        if (cause == "no_such_user")
        {
            _userManagerMock.Setup(x => x.FindByEmailAsync(Email)).ReturnsAsync((ApplicationUser?)null);
            return;
        }

        var user = ArrangeSignInReadyUser();

        switch (cause)
        {
            case "deactivated":
                user.Deactivate();
                break;
            case "locked_out":
                _userManagerMock.Setup(x => x.IsLockedOutAsync(user)).ReturnsAsync(true);
                break;
            case "cannot_sign_in":
                _signInManagerMock.Setup(x => x.CanSignInAsync(user)).ReturnsAsync(false);
                break;
            case "wrong_password":
                _userManagerMock.Setup(x => x.CheckPasswordAsync(user, Password)).ReturnsAsync(false);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(cause), cause, "unknown arrangement");
        }
    }

    /// <summary>A rejected login must issue nothing, whatever the cause.</summary>
    [Test]
    public async Task Handle_WithAWrongPassword_IssuesNoTokens()
    {
        var user = ArrangeSignInReadyUser();
        _userManagerMock.Setup(x => x.CheckPasswordAsync(user, Password)).ReturnsAsync(false);

        await _handler.Handle(Command(), CancellationToken.None);

        _tokenServiceMock.Verify(
            x => x.GenerateAccessTokenAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()), Times.Never);
        _tokenServiceMock.Verify(
            x => x.GenerateRefreshTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ---------------------------------------------------------------------------------------
    // (2) The brute-force gate comes first
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The gate is only worth having if it short-circuits. If the lookup and password check ran
    /// first, a blocked attacker would still get a working oracle — and would still be burning a
    /// PBKDF2 verification per request, which is the denial-of-service half of the same problem.
    /// </summary>
    [Test]
    public async Task Handle_WhenTheAttemptIsBlocked_DoesNoCredentialWorkAtAll()
    {
        _trackerMock
            .Setup(x => x.ValidateAttemptAsync(Email, Ip, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginAttemptValidationResult.Blocked(
                BlockReason.AccountLocked, failedAttempts: 9, expiresAt: DateTime.UtcNow.AddMinutes(15),
                message: "Account temporarily locked"));

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Auth.TooManyAttempts"));
        _userManagerMock.Verify(x => x.FindByEmailAsync(It.IsAny<string>()), Times.Never);
        _userManagerMock.Verify(
            x => x.CheckPasswordAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    /// <summary>
    /// An already-blocked attempt must not be counted again. Recording it would let an attacker
    /// keep their own lockout alive indefinitely by continuing to hammer a blocked account — and
    /// on the throttling layer it would push the backoff up without a credential ever being tried.
    /// </summary>
    [Test]
    public async Task Handle_WhenTheAttemptIsBlocked_DoesNotRecordAFurtherFailure()
    {
        _trackerMock
            .Setup(x => x.ValidateAttemptAsync(Email, Ip, It.IsAny<CancellationToken>()))
            .ReturnsAsync(LoginAttemptValidationResult.Throttled(
                delaySeconds: 8, failedAttempts: 4, message: "Please wait"));

        await _handler.Handle(Command(), CancellationToken.None);

        _trackerMock.Verify(
            x => x.RecordFailedAttemptAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>
    /// Conversely, a rejection that did reach the credential check has to be counted, or the
    /// lockout never trips and the whole tracker is decorative.
    /// </summary>
    [Test]
    public async Task Handle_WithAWrongPassword_RecordsTheFailedAttempt()
    {
        var user = ArrangeSignInReadyUser();
        _userManagerMock.Setup(x => x.CheckPasswordAsync(user, Password)).ReturnsAsync(false);

        await _handler.Handle(Command(), CancellationToken.None);

        _trackerMock.Verify(x => x.RecordFailedAttemptAsync(Email, Ip, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// The easily-missed one: 2FA rejection happens after the password already passed, on a
    /// separate return path. If it skipped the tracker, the second factor would be brute-forceable
    /// at unlimited rate by anyone holding a valid password — six digits, no lockout.
    /// </summary>
    [Test]
    public async Task Handle_WithAnInvalid2FACode_RecordsTheFailedAttempt()
    {
        var user = ArrangeSignInReadyUser();
        user.TwoFactorEnabled = true;
        _userManagerMock
            .Setup(x => x.VerifyTwoFactorTokenAsync(user, It.IsAny<string>(), "000000"))
            .ReturnsAsync(false);

        var result = await _handler.Handle(Command(twoFactorCode: "000000"), CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Auth.Invalid2FA"));
        _trackerMock.Verify(x => x.RecordFailedAttemptAsync(Email, Ip, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------------------------------------------------------------------------------------
    // (3) The 2FA challenge issues nothing
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// This branch returns <c>Success</c>, which is the trap: it is a challenge, not an
    /// authentication. Anything populated on the response here is handed to a caller who has
    /// presented one factor, so a token leaking into it would reduce 2FA to an opt-in prompt the
    /// client could simply ignore.
    /// </summary>
    [Test]
    public async Task Handle_When2FAIsRequired_ReturnsAChallengeCarryingNoTokensOrUserData()
    {
        var user = ArrangeSignInReadyUser();
        user.TwoFactorEnabled = true;

        var result = await _handler.Handle(Command(twoFactorCode: null), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Requires2FA, Is.True);
        Assert.That(result.Value.AccessToken, Is.Empty);
        Assert.That(result.Value.RefreshToken, Is.Empty);
        Assert.That(result.Value.User, Is.Null, "the challenge must not disclose the account it belongs to");

        _tokenServiceMock.Verify(
            x => x.GenerateAccessTokenAsync(It.IsAny<ApplicationUser>(), It.IsAny<CancellationToken>()), Times.Never);
        _tokenServiceMock.Verify(
            x => x.GenerateRefreshTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    /// <summary>A challenge is not a completed login, so it must not clear the failure counters.</summary>
    [Test]
    public async Task Handle_When2FAIsRequired_DoesNotClearTheFailureCounters()
    {
        var user = ArrangeSignInReadyUser();
        user.TwoFactorEnabled = true;

        await _handler.Handle(Command(twoFactorCode: null), CancellationToken.None);

        _trackerMock.Verify(
            x => x.RecordSuccessfulLoginAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ---------------------------------------------------------------------------------------
    // Success path
    // ---------------------------------------------------------------------------------------

    [Test]
    public async Task Handle_WithValidCredentials_IssuesTokensAndClearsTheFailureCounters()
    {
        ArrangeSignInReadyUser();

        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Requires2FA, Is.False);
        Assert.That(result.Value.AccessToken, Is.EqualTo("access-token"));
        Assert.That(result.Value.RefreshToken, Is.EqualTo("refresh-token"));
        Assert.That(result.Value.User!.Roles, Is.EqualTo(new[] { "Customer" }));
        _trackerMock.Verify(x => x.RecordSuccessfulLoginAsync(Email, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Guards the concurrent-login regression. Last-login is telemetry and must be written through
    /// the repository's <c>ExecuteUpdateAsync</c> path: <c>UserManager.UpdateAsync</c> puts
    /// ASP.NET Identity's <c>ConcurrencyStamp</c> in the WHERE clause, so simultaneous logins by
    /// one user raced, and the loser left a tracked entity with a stale stamp that
    /// <c>TransactionBehavior</c>'s commit re-issued — surfacing as a 500 from an otherwise valid
    /// login. Ten concurrent logins produced one success and nine 500s.
    /// </summary>
    [Test]
    public async Task Handle_WithValidCredentials_WritesLastLoginWithoutTouchingTheConcurrencyStamp()
    {
        var user = ArrangeSignInReadyUser();

        await _handler.Handle(Command(), CancellationToken.None);

        _userRepositoryMock.Verify(
            x => x.UpdateLastLoginAsync(user.Id, It.IsAny<DateTime>(), Ip, It.IsAny<CancellationToken>()),
            Times.Once);
        _userManagerMock.Verify(x => x.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never,
            "UpdateAsync would reintroduce the ConcurrencyStamp race this path exists to avoid");
    }

    // ---------------------------------------------------------------------------------------

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

    private static Mock<SignInManager<ApplicationUser>> MockSignInManager(
        Mock<UserManager<ApplicationUser>> userManagerMock)
    {
        var optionsAccessor = new Mock<IOptions<IdentityOptions>>();
        optionsAccessor.Setup(x => x.Value).Returns(new IdentityOptions());

        return new Mock<SignInManager<ApplicationUser>>(
            userManagerMock.Object,
            new Mock<IHttpContextAccessor>().Object,
            new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>().Object,
            optionsAccessor.Object,
            new Mock<ILogger<SignInManager<ApplicationUser>>>().Object,
            new Mock<IAuthenticationSchemeProvider>().Object,
            new Mock<IUserConfirmation<ApplicationUser>>().Object);
    }
}
