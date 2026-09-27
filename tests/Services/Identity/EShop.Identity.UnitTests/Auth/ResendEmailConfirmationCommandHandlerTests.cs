using EShop.BuildingBlocks.Application.Abstractions;
using EShop.BuildingBlocks.Messaging;
using EShop.BuildingBlocks.Messaging.Events;
using EShop.Identity.Application.Auth.Commands.ResendEmailConfirmation;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Identity.UnitTests.Auth;

/// <summary>
/// <c>POST /api/v1/auth/resend-confirmation</c>. Two properties matter and both fail silently:
/// only an active, unconfirmed account outside its cooldown is sent anything, and <b>every</b>
/// outcome looks identical to the caller — otherwise the endpoint enumerates accounts, or tells a
/// stranger which of them never confirmed.
/// </summary>
[TestFixture]
public class ResendEmailConfirmationCommandHandlerTests
{
    private const string Email = "pending@test.com";
    private const string UserId = "user-1";
    private const string Token = "fresh-confirmation-token";
    private const string CorrelationId = "corr-resend";

    private Mock<UserManager<ApplicationUser>> _userManagerMock = null!;
    private Mock<IEmailConfirmationResendThrottle> _throttleMock = null!;
    private Mock<IIntegrationEventOutbox> _outboxMock = null!;
    private ResendEmailConfirmationCommandHandler _handler = null!;
    private ApplicationUser _user = null!;

    [SetUp]
    public void SetUp()
    {
        _userManagerMock = MockUserManager();
        _throttleMock = new Mock<IEmailConfirmationResendThrottle>();
        _outboxMock = new Mock<IIntegrationEventOutbox>();
        var currentUserContext = new Mock<ICurrentUserContext>();
        currentUserContext.SetupGet(x => x.CorrelationId).Returns(CorrelationId);

        // Default: an active account that has not confirmed, and a free resend slot.
        _user = new ApplicationUser { Id = UserId, Email = Email, UserName = Email, EmailConfirmed = false };
        _userManagerMock.Setup(x => x.FindByEmailAsync(Email)).ReturnsAsync(_user);
        _userManagerMock.Setup(x => x.GenerateEmailConfirmationTokenAsync(_user)).ReturnsAsync(Token);
        _throttleMock.Setup(x => x.TryAcquireAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        _handler = new ResendEmailConfirmationCommandHandler(
            _userManagerMock.Object,
            _throttleMock.Object,
            _outboxMock.Object,
            currentUserContext.Object,
            Mock.Of<ILogger<ResendEmailConfirmationCommandHandler>>());
    }

    private static ResendEmailConfirmationCommand Command(string email = Email) => new() { Email = email };

    /// <summary>What a real send answers; every refusal below must answer exactly this too.</summary>
    private static readonly ResendEmailConfirmationResponse Uniform = new()
    {
        Success = true,
        Message = ResendEmailConfirmationCommandHandler.UniformMessage
    };

    [Test]
    public async Task AnUnconfirmedAccount_IsSentAFreshToken()
    {
        var result = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value, Is.EqualTo(Uniform));
        _outboxMock.Verify(x => x.Enqueue(
            It.Is<EmailConfirmationRequestedIntegrationEvent>(e =>
                e.UserId == UserId && e.ConfirmationToken == Token && e.CorrelationId == CorrelationId),
            CorrelationId), Times.Once);
        _outboxMock.Verify(x => x.Enqueue(It.IsAny<IIntegrationEvent>(), It.IsAny<string?>()), Times.Once,
            "one email, not a second 'user registered'");
    }

    /// <summary>
    /// Each case gets the success a real send gets, and nothing is enqueued. The literal response is
    /// compared, because the enumeration channel is whatever reaches the client.
    /// </summary>
    [TestCase("unknown")]
    [TestCase("deactivated")]
    [TestCase("already_confirmed")]
    [TestCase("in_cooldown")]
    public async Task EveryRefusal_AnswersExactlyLikeASend_AndEnqueuesNothing(string cause)
    {
        switch (cause)
        {
            case "unknown":
                _userManagerMock.Setup(x => x.FindByEmailAsync(Email)).ReturnsAsync((ApplicationUser?)null);
                break;
            case "deactivated":
                _user.Deactivate();
                break;
            case "already_confirmed":
                _user.EmailConfirmed = true;
                break;
            case "in_cooldown":
                _throttleMock.Setup(x => x.TryAcquireAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
                break;
        }

        var refused = await _handler.Handle(Command(), CancellationToken.None);

        Assert.That(refused.IsSuccess, Is.True, cause);
        Assert.That(refused.Value, Is.EqualTo(Uniform), $"{cause}: the response must not differ from a real send");
        _outboxMock.Verify(x => x.Enqueue(It.IsAny<IIntegrationEvent>(), It.IsAny<string?>()), Times.Never, cause);
    }

    /// <summary>
    /// The cooldown is claimed only for an account that would actually get an email — an unknown or
    /// already-confirmed address must never start one, or the throttle itself becomes the oracle.
    /// </summary>
    [TestCase("unknown")]
    [TestCase("deactivated")]
    [TestCase("already_confirmed")]
    public async Task AnAccountWithNothingToConfirm_NeverTouchesTheThrottle(string cause)
    {
        switch (cause)
        {
            case "unknown":
                _userManagerMock.Setup(x => x.FindByEmailAsync(Email)).ReturnsAsync((ApplicationUser?)null);
                break;
            case "deactivated":
                _user.Deactivate();
                break;
            case "already_confirmed":
                _user.EmailConfirmed = true;
                break;
        }

        await _handler.Handle(Command(), CancellationToken.None);

        _throttleMock.Verify(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Refused paths still generate a (discarded) token, so the work done is comparable to a send —
    /// but never for the real account, whose token must not be minted for nothing.
    /// </summary>
    [TestCase("unknown")]
    [TestCase("in_cooldown")]
    public async Task ARefusedPath_DoesComparableWork_WithoutMintingTheRealAccountsToken(string cause)
    {
        if (cause == "unknown")
            _userManagerMock.Setup(x => x.FindByEmailAsync(Email)).ReturnsAsync((ApplicationUser?)null);
        else
            _throttleMock.Setup(x => x.TryAcquireAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await _handler.Handle(Command(), CancellationToken.None);

        _userManagerMock.Verify(x => x.GenerateEmailConfirmationTokenAsync(It.IsAny<ApplicationUser>()), Times.Once);
        _userManagerMock.Verify(x => x.GenerateEmailConfirmationTokenAsync(_user), Times.Never);
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
