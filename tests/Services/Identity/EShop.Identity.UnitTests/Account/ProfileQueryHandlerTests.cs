using System.Text.Json;
using EShop.Identity.Application.Account.Queries.GetProfile;
using EShop.Identity.Application.Users.Queries.GetUserContact;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Identity.UnitTests.Account;

/// <summary>
/// TEST-06, closing the last two untested handlers. Both are thin read paths, and both hand-map
/// an <see cref="ApplicationUser"/> into a response record — which is exactly why they are worth
/// pinning. A hand-written projection has no compiler telling it which columns are safe to emit,
/// so the guard has to be a test.
///
/// <para>
/// The two share one property. <c>GetProfile</c> is what a user sees about themselves;
/// <c>GetUserContact</c> is what <b>Notification</b> reads over the InternalService API-key
/// policy. Neither may emit a credential field, and a deactivated account must be invisible to
/// both — otherwise deactivation stops meaning anything: the account still answers, and an
/// internal caller can still harvest the address to mail it.
/// </para>
/// </summary>
[TestFixture]
public class ProfileQueryHandlerTests
{
    private const string UserId = "user-1";

    /// <summary>
    /// Fields that must never reach a response body. <c>PasswordHash</c> and
    /// <c>SecurityStamp</c> come from <c>IdentityUser</c> and are present on every entity these
    /// handlers touch, so "we didn't map it" is the only thing keeping them out.
    /// </summary>
    private static readonly string[] ForbiddenFields =
    [
        "passwordHash", "securityStamp", "concurrencyStamp", "twoFactorSecret", "accessFailedCount"
    ];

    private static ApplicationUser ActiveUser() => new()
    {
        Id = UserId,
        Email = "user@test.com",
        UserName = "user@test.com",
        FirstName = "Ada",
        LastName = "Lovelace",
        EmailConfirmed = true,
        PasswordHash = "AQAAAAIAAYagAAAAE-not-a-real-hash",
        SecurityStamp = "SECURITYSTAMPVALUE"
    };

    // -------------------------------------------------------------------------------------
    // GetProfile
    // -------------------------------------------------------------------------------------

    [Test]
    public async Task GetProfile_ForAnActiveUser_ReturnsTheProfileWithRoles()
    {
        var user = ActiveUser();
        var userManager = MockUserManager();
        userManager.Setup(x => x.FindByIdAsync(UserId)).ReturnsAsync(user);
        userManager.Setup(x => x.GetRolesAsync(user)).ReturnsAsync(new List<string> { "User", "Admin" });

        var result = await new GetProfileQueryHandler(userManager.Object)
            .Handle(new GetProfileQuery { UserId = UserId }, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Id, Is.EqualTo(UserId));
        Assert.That(result.Value.Email, Is.EqualTo("user@test.com"));
        Assert.That(result.Value.Roles, Is.EqualTo(new[] { "User", "Admin" }));
    }

    /// <summary>
    /// The response is hand-mapped from an entity that carries the password hash and security
    /// stamp, so nothing but the absence of those two lines keeps them out of the body. Serialising
    /// and searching catches a field added later far more reliably than reading the mapping.
    /// </summary>
    [Test]
    public async Task GetProfile_LeaksNoCredentialFields()
    {
        var user = ActiveUser();
        var userManager = MockUserManager();
        userManager.Setup(x => x.FindByIdAsync(UserId)).ReturnsAsync(user);
        userManager.Setup(x => x.GetRolesAsync(user)).ReturnsAsync(new List<string>());

        var result = await new GetProfileQueryHandler(userManager.Object)
            .Handle(new GetProfileQuery { UserId = UserId }, CancellationToken.None);

        AssertCarriesNoSecrets(result.Value);
    }

    /// <summary>
    /// The code must match what the password and refresh handlers answer for the same state.
    /// This query used to return <c>Account.Disabled</c> while those three returned
    /// <c>Auth.AccountDisabled</c>, so a client had to special-case one endpoint to recognise a
    /// condition that is identical everywhere else.
    /// </summary>
    [Test]
    public async Task GetProfile_ForADeactivatedAccount_IsRefusedWithTheSameCodeAsEverywhereElse()
    {
        var user = ActiveUser();
        user.Deactivate();
        var userManager = MockUserManager();
        userManager.Setup(x => x.FindByIdAsync(UserId)).ReturnsAsync(user);

        var result = await new GetProfileQueryHandler(userManager.Object)
            .Handle(new GetProfileQuery { UserId = UserId }, CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Auth.AccountDisabled"),
            "must match ChangePassword, ResetPassword and RefreshToken for the same account state");
    }

    [Test]
    public async Task GetProfile_ForAMissingUser_ReturnsNotFound()
    {
        var userManager = MockUserManager();
        userManager.Setup(x => x.FindByIdAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);

        var result = await new GetProfileQueryHandler(userManager.Object)
            .Handle(new GetProfileQuery { UserId = UserId }, CancellationToken.None);

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Account.NotFound"));
    }

    // -------------------------------------------------------------------------------------
    // GetUserContact — the InternalService read path
    // -------------------------------------------------------------------------------------

    [Test]
    public async Task GetUserContact_ForAnActiveUser_ReturnsTheContactAndNothingElse()
    {
        var repository = new Mock<IUserRepository>();
        repository.Setup(x => x.GetByIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ActiveUser());

        var result = await new GetUserContactQueryHandler(repository.Object)
            .Handle(new GetUserContactQuery { UserId = UserId }, CancellationToken.None);

        Assert.That(result.IsSuccess, Is.True);
        Assert.That(result.Value.Id, Is.EqualTo(UserId),
            "DEBT-18: the key is Id, matching UserProfileResponse and the login response's UserDto");
        Assert.That(result.Value.Email, Is.EqualTo("user@test.com"));
        AssertCarriesNoSecrets(result.Value);
    }

    /// <summary>
    /// Three different reasons, one answer — the same uniformity login enforces, and for the same
    /// motive: this endpoint is reachable by any holder of the InternalService API key, so a
    /// distinguishable "disabled" reply would confirm that an address is registered.
    /// </summary>
    [TestCase("missing")]
    [TestCase("deactivated")]
    [TestCase("no_email")]
    public async Task GetUserContact_ForAnyUnreachableUser_GivesTheSameOpaqueAnswer(string cause)
    {
        var repository = new Mock<IUserRepository>();
        repository.Setup(x => x.GetByIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ArrangeUnreachable(cause));

        var result = await new GetUserContactQueryHandler(repository.Object)
            .Handle(new GetUserContactQuery { UserId = UserId }, CancellationToken.None);

        Assert.That(result.IsFailure, Is.True, $"'{cause}' must not yield a contact");
        Assert.That(result.Error!.Code, Is.EqualTo("Users.ContactNotFound"));
    }

    private static ApplicationUser? ArrangeUnreachable(string cause)
    {
        switch (cause)
        {
            case "missing":
                return null;
            case "deactivated":
                var deactivated = ActiveUser();
                deactivated.Deactivate();
                return deactivated;
            case "no_email":
                var noEmail = ActiveUser();
                noEmail.Email = null;
                return noEmail;
            default:
                throw new ArgumentOutOfRangeException(nameof(cause), cause, "unknown arrangement");
        }
    }

    // -------------------------------------------------------------------------------------

    private static void AssertCarriesNoSecrets(object? response)
    {
        var payload = JsonSerializer.Serialize(response);

        foreach (var field in ForbiddenFields)
        {
            Assert.That(payload, Does.Not.Contain(field).IgnoreCase,
                $"'{field}' must never appear in a response body");
        }

        Assert.That(payload, Does.Not.Contain("not-a-real-hash"),
            "the password hash value itself must not appear under any property name");
        Assert.That(payload, Does.Not.Contain("SECURITYSTAMPVALUE"));
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
