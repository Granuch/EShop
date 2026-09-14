using EShop.Identity.Application.Account.Commands.UpdateProfile;
using EShop.Identity.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Identity.UnitTests.Account;

/// <summary>
/// TEST-06, guarding BUG-09.
///
/// <para>
/// The handler used to assign <c>ProfilePictureUrl</c> unconditionally. System.Text.Json
/// materialises an <i>omitted</i> optional property as <c>null</c>, so any client sending a
/// partial update — name only, say — silently cleared the stored avatar. Nothing failed and
/// nothing logged; the picture simply disappeared.
/// </para>
///
/// <para>
/// This is the scalar case of the repo-wide "optional members must tolerate explicit null" rule,
/// and it is easy to reintroduce because the fix looks like a redundant null check. The three
/// tests below pin the whole contract: omitted leaves the value alone, empty string clears it
/// deliberately, and a real value replaces it.
/// </para>
/// </summary>
[TestFixture]
public class UpdateProfileCommandHandlerTests
{
    private const string ExistingPicture = "https://cdn.test/avatar.png";

    private Mock<UserManager<ApplicationUser>> _userManagerMock = null!;
    private UpdateProfileCommandHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _userManagerMock = MockUserManager();
        _handler = new UpdateProfileCommandHandler(
            _userManagerMock.Object,
            new Mock<ILogger<UpdateProfileCommandHandler>>().Object);
    }

    /// <summary>Returns the user the handler will mutate, so a test can inspect it afterwards.</summary>
    private ApplicationUser ArrangeExistingUser()
    {
        var user = new ApplicationUser
        {
            Id = "user-1",
            Email = "user@test.com",
            FirstName = "Original",
            LastName = "Name",
            ProfilePictureUrl = ExistingPicture
        };

        _userManagerMock.Setup(x => x.FindByIdAsync(user.Id)).ReturnsAsync(user);
        _userManagerMock.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Success);

        return user;
    }

    private static UpdateProfileCommand Command(string? profilePictureUrl) => new()
    {
        UserId = "user-1",
        FirstName = "Updated",
        LastName = "Person",
        ProfilePictureUrl = profilePictureUrl
    };

    /// <summary>BUG-09 itself: a partial update must not wipe the avatar.</summary>
    [Test]
    public async Task Handle_WithOmittedProfilePictureUrl_LeavesTheExistingPictureIntact()
    {
        var user = ArrangeExistingUser();

        var result = await _handler.Handle(Command(profilePictureUrl: null), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(user.ProfilePictureUrl, Is.EqualTo(ExistingPicture),
            "an omitted optional property arrives as null and must not be treated as 'clear this'");
        Assert.That(user.FirstName, Is.EqualTo("Updated"), "the fields that were sent still apply");
        Assert.That(user.LastName, Is.EqualTo("Person"));
    }

    /// <summary>
    /// The deliberate-clear path. Omitting the field means "leave it", so there has to be some
    /// way to actually remove a picture — an empty string is it.
    /// </summary>
    [Test]
    public async Task Handle_WithEmptyProfilePictureUrl_ClearsThePictureDeliberately()
    {
        var user = ArrangeExistingUser();

        var result = await _handler.Handle(Command(profilePictureUrl: string.Empty), CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(user.ProfilePictureUrl, Is.Null);
    }

    [Test]
    public async Task Handle_WithWhitespaceProfilePictureUrl_ClearsThePicture()
    {
        var user = ArrangeExistingUser();

        await _handler.Handle(Command(profilePictureUrl: "   "), CancellationToken.None);

        Assert.That(user.ProfilePictureUrl, Is.Null, "whitespace is treated as a clear, not as a URL");
    }

    [Test]
    public async Task Handle_WithANewProfilePictureUrl_ReplacesThePicture()
    {
        var user = ArrangeExistingUser();

        await _handler.Handle(Command(profilePictureUrl: "https://cdn.test/new.png"), CancellationToken.None);

        Assert.That(user.ProfilePictureUrl, Is.EqualTo("https://cdn.test/new.png"));
    }

    [Test]
    public async Task Handle_WithNonExistentUser_ReturnsNotFound()
    {
        _userManagerMock.Setup(x => x.FindByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((ApplicationUser?)null);

        var result = await _handler.Handle(Command(null), CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Account.NotFound"));
    }

    [Test]
    public async Task Handle_WhenTheUpdateFails_SurfacesTheFailureRatherThanReportingSuccess()
    {
        ArrangeExistingUser();
        _userManagerMock.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "store rejected it" }));

        var result = await _handler.Handle(Command(null), CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Account.UpdateFailed"));
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
