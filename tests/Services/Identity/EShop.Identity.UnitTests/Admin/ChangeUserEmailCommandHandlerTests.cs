using EShop.Identity.Application.Users.Commands.ChangeUserEmail;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Identity.UnitTests.Admin;

/// <summary>
/// Admin panel S7 — the email-change command, where the ordering of two lines is the whole test.
/// </summary>
/// <remarks>
/// <b>Why a duplicate has to be refused before the entity is touched.</b>
/// <c>UserManager.UpdateAsync</c> validates and only then saves, so on a duplicate it returns a
/// failed <c>IdentityResult</c> having written nothing — but the entity is already mutated and
/// tracked, and <c>TransactionBehavior</c> commits on a <c>Result</c> failure. Relying on
/// <c>UpdateAsync</c> to reject the duplicate therefore persists it while answering 409. The
/// assertions below check the <i>entity</i>, not the status code, because the status code is
/// identical either way.
/// </remarks>
[TestFixture]
public class ChangeUserEmailCommandHandlerTests
{
    private const string UserId = "user-1";
    private const string OriginalEmail = "original@test.com";

    private Mock<UserManager<ApplicationUser>> _userManager = null!;
    private Mock<IUserRepository> _userRepository = null!;
    private ChangeUserEmailCommandHandler _handler = null!;
    private ApplicationUser _user = null!;

    [SetUp]
    public void SetUp()
    {
        _user = new ApplicationUser
        {
            Id = UserId,
            Email = OriginalEmail,
            UserName = OriginalEmail,
            EmailConfirmed = true
        };

        _userManager = MockUserManager();
        _userManager.Setup(x => x.FindByIdAsync(UserId)).ReturnsAsync(_user);
        _userManager.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);

        _userRepository = new Mock<IUserRepository>();
        _userRepository
            .Setup(x => x.EmailIsTakenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _handler = new ChangeUserEmailCommandHandler(
            _userManager.Object,
            _userRepository.Object,
            new Mock<ILogger<ChangeUserEmailCommandHandler>>().Object);
    }

    private Task<EShop.BuildingBlocks.Application.Result<MediatR.Unit>> HandleAsync(
        string email, bool markConfirmed = false)
        => _handler.Handle(
            new ChangeUserEmailCommand { UserId = UserId, Email = email, MarkConfirmed = markConfirmed },
            CancellationToken.None);

    [Test]
    public async Task Handle_MovesTheUserNameWithTheEmail()
    {
        var result = await HandleAsync("new@test.com");

        Assert.That(result.IsSuccess, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(_user.Email, Is.EqualTo("new@test.com"));
            Assert.That(_user.UserName, Is.EqualTo("new@test.com"),
                "login looks accounts up by email while the unique index is on the user name, so the "
                + "two cannot be allowed to drift");
        });
    }

    [Test]
    public async Task Handle_WithoutMarkConfirmed_ClearsTheConfirmation()
    {
        await HandleAsync("new@test.com");

        Assert.That(_user.EmailConfirmed, Is.False,
            "the new address has not been verified, and defaulting to 'unconfirmed' is the safe half");
    }

    [Test]
    public async Task Handle_WithMarkConfirmed_KeepsTheAddressConfirmed()
    {
        await HandleAsync("new@test.com", markConfirmed: true);

        Assert.That(_user.EmailConfirmed, Is.True);
    }

    [Test]
    public async Task Handle_WithATakenEmail_LeavesTheStoredAddressUntouched()
    {
        _userRepository
            .Setup(x => x.EmailIsTakenAsync("taken@test.com", UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await HandleAsync("taken@test.com");

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("User.EmailConflict"));
        Assert.Multiple(() =>
        {
            // The assertion that matters: TransactionBehavior would have committed a mutated entity.
            Assert.That(_user.Email, Is.EqualTo(OriginalEmail));
            Assert.That(_user.UserName, Is.EqualTo(OriginalEmail));
            Assert.That(_user.EmailConfirmed, Is.True);
        });
        _userManager.Verify(x => x.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Test]
    public async Task Handle_ExcludesTheUserBeingEdited_SoResendingItsOwnAddressIsAllowed()
    {
        await HandleAsync(OriginalEmail, markConfirmed: true);

        _userRepository.Verify(
            x => x.EmailIsTakenAsync(OriginalEmail, UserId, It.IsAny<CancellationToken>()), Times.Once,
            "without the exclusion the user's own row answers 'taken' and nobody could re-save their address");
    }

    [Test]
    public async Task Handle_TrimsTheAddress()
    {
        await HandleAsync("  spaced@test.com  ");

        Assert.That(_user.Email, Is.EqualTo("spaced@test.com"));
    }

    [Test]
    public async Task Handle_WithAnUnknownUser_IsNotFound()
    {
        _userManager.Setup(x => x.FindByIdAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);

        var result = await HandleAsync("new@test.com");

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("User.NotFound"));
    }

    /// <summary>
    /// A store-level rejection after the entity is already dirty must throw, not return a failure —
    /// a <c>Result</c> failure there is committed by <c>TransactionBehavior</c>.
    /// </summary>
    [Test]
    public void Handle_WhenTheStoreRejectsTheUpdate_ThrowsSoTheTransactionRollsBack()
    {
        _userManager.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "store rejected it" }));

        Assert.ThrowsAsync<InvalidOperationException>(() => HandleAsync("new@test.com"));
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
