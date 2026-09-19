using EShop.Identity.Application.Users.Commands.SetUserRoles;
using EShop.Identity.Domain.Entities;
using EShop.Identity.Domain.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace EShop.Identity.UnitTests.Admin;

/// <summary>
/// Admin panel S7 — the role-replacement command, whose interesting behaviour is all about
/// <i>when</i> it writes rather than what it writes.
/// </summary>
/// <remarks>
/// Two contracts are pinned here and neither is visible from a status code.
/// <list type="number">
/// <item><b>Every role name is validated before a single membership row moves.</b>
/// <c>TransactionBehavior</c> commits on a <c>Result</c> failure, so a handler that discovered an
/// unknown role halfway through its loop and returned a failure would leave a half-applied role set
/// behind while telling the caller nothing happened.</item>
/// <item><b>The roles cache is invalidated through its owning service, not through
/// <c>ICacheInvalidatingCommand</c>.</b> That marker can only evict what <c>CachingBehavior</c>
/// wrote, and <c>CachedUserRolesService</c> owns an unprefixed namespace — declaring the key would
/// remove nothing and log success (SEC-02).</item>
/// </list>
/// </remarks>
[TestFixture]
public class SetUserRolesCommandHandlerTests
{
    private const string UserId = "user-1";

    private Mock<UserManager<ApplicationUser>> _userManager = null!;
    private Mock<RoleManager<ApplicationRole>> _roleManager = null!;
    private Mock<ICachedUserRolesService> _cachedUserRoles = null!;
    private SetUserRolesCommandHandler _handler = null!;
    private ApplicationUser _user = null!;

    [SetUp]
    public void SetUp()
    {
        _user = new ApplicationUser { Id = UserId, Email = "user@test.com", UserName = "user@test.com" };

        _userManager = MockUserManager();
        _userManager.Setup(x => x.FindByIdAsync(UserId)).ReturnsAsync(_user);
        _userManager.Setup(x => x.AddToRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);
        _userManager.Setup(x => x.RemoveFromRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync(IdentityResult.Success);

        _roleManager = MockRoleManager();
        _cachedUserRoles = new Mock<ICachedUserRolesService>();

        _handler = new SetUserRolesCommandHandler(
            _userManager.Object,
            _roleManager.Object,
            _cachedUserRoles.Object,
            new Mock<ILogger<SetUserRolesCommandHandler>>().Object);
    }

    private void ArrangeCurrentRoles(params string[] roles)
        => _userManager.Setup(x => x.GetRolesAsync(_user)).ReturnsAsync(roles.ToList());

    private void ArrangeExistingRoles(params string[] roles)
    {
        _roleManager.Setup(x => x.RoleExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        foreach (var role in roles)
        {
            _roleManager.Setup(x => x.RoleExistsAsync(role)).ReturnsAsync(true);
        }
    }

    private Task<EShop.BuildingBlocks.Application.Result<MediatR.Unit>> HandleAsync(params string[] roles)
        => _handler.Handle(new SetUserRolesCommand { UserId = UserId, Roles = roles }, CancellationToken.None);

    [Test]
    public async Task Handle_AddsAndRemovesOnlyTheDifference()
    {
        ArrangeExistingRoles("Admin", "User", "Support");
        ArrangeCurrentRoles("User", "Support");

        var result = await HandleAsync("User", "Admin");

        Assert.That(result.IsSuccess, Is.True);
        _userManager.Verify(x => x.AddToRolesAsync(_user, It.Is<IEnumerable<string>>(r => r.SequenceEqual(new[] { "Admin" }))), Times.Once);
        _userManager.Verify(x => x.RemoveFromRolesAsync(_user, It.Is<IEnumerable<string>>(r => r.SequenceEqual(new[] { "Support" }))), Times.Once);
    }

    [Test]
    public async Task Handle_WithAnUnchangedSet_WritesNothing_AndDoesNotEvictTheRolesCache()
    {
        ArrangeExistingRoles("Admin", "User");
        ArrangeCurrentRoles("User", "Admin");

        var result = await HandleAsync("Admin", "User");

        Assert.That(result.IsSuccess, Is.True);
        _userManager.Verify(x => x.AddToRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()), Times.Never);
        _userManager.Verify(x => x.RemoveFromRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()), Times.Never);
        _cachedUserRoles.Verify(x => x.InvalidateRolesCacheAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Handle_WithAnEmptySet_StripsEveryRole()
    {
        ArrangeExistingRoles("Admin", "User");
        ArrangeCurrentRoles("Admin", "User");

        var result = await HandleAsync();

        Assert.That(result.IsSuccess, Is.True);
        _userManager.Verify(x => x.RemoveFromRolesAsync(_user,
            It.Is<IEnumerable<string>>(r => r.OrderBy(n => n).SequenceEqual(new[] { "Admin", "User" }))), Times.Once);
    }

    /// <summary>
    /// The atomicity contract. The unknown name is second in the list precisely so that a handler
    /// which validated as it went would already have granted the first one.
    /// </summary>
    [Test]
    public async Task Handle_WithAnUnknownRole_ChangesNothingAtAll()
    {
        ArrangeExistingRoles("Admin", "User");
        ArrangeCurrentRoles("User");

        var result = await HandleAsync("Admin", "Wizard");

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("Role.NotFound"));
        _userManager.Verify(x => x.AddToRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()), Times.Never);
        _userManager.Verify(x => x.RemoveFromRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()), Times.Never);
        _cachedUserRoles.Verify(x => x.InvalidateRolesCacheAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task Handle_WhenRolesChange_InvalidatesTheRolesCacheItself()
    {
        // Not via ICacheInvalidatingCommand: that marker cannot reach CachedUserRolesService's
        // unprefixed key, so the tokens minted for the next five minutes would keep the old roles.
        ArrangeExistingRoles("Admin", "User");
        ArrangeCurrentRoles("User");

        await HandleAsync("Admin");

        _cachedUserRoles.Verify(x => x.InvalidateRolesCacheAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Handle_MatchesExistingRolesCaseInsensitively_AndDeduplicates()
    {
        ArrangeExistingRoles("Admin", "admin", "User");
        ArrangeCurrentRoles("Admin");

        var result = await HandleAsync(" Admin ", "admin");

        Assert.That(result.IsSuccess, Is.True);
        _userManager.Verify(x => x.AddToRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()), Times.Never,
            "the user already holds the only role asked for, however it was spelled or padded");
        _userManager.Verify(x => x.RemoveFromRolesAsync(It.IsAny<ApplicationUser>(), It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Test]
    public async Task Handle_WithAnUnknownUser_IsNotFound()
    {
        _userManager.Setup(x => x.FindByIdAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);

        var result = await HandleAsync("Admin");

        Assert.That(result.IsFailure, Is.True);
        Assert.That(result.Error!.Code, Is.EqualTo("User.NotFound"));
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

    /// <summary>
    /// Only the store argument has to be real — <c>RoleManager</c>'s constructor null-checks that
    /// one and merely assigns the rest. Every method used here is virtual, so Moq intercepts it and
    /// the base implementation (which would dereference the null normaliser) never runs.
    /// </summary>
    private static Mock<RoleManager<ApplicationRole>> MockRoleManager()
    {
        var store = new Mock<IRoleStore<ApplicationRole>>();

        return new Mock<RoleManager<ApplicationRole>>(
            store.Object,
            null!, null!, null!, null!);
    }
}
