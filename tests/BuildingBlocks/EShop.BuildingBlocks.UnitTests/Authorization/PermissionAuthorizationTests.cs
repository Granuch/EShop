using System.Security.Claims;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace EShop.BuildingBlocks.UnitTests.Authorization;

/// <summary>
/// The permission model introduced by decision Q4c. These tests exist because the whole migration
/// rests on one invariant that fails <i>silently</i> when broken: an <c>Admin</c> whose bundle has
/// drifted simply starts getting 403s on one endpoint, with nothing logged and no other test
/// failing.
/// </summary>
[TestFixture]
public class PermissionAuthorizationTests
{
    private PermissionAuthorizationHandler _handler = null!;

    [SetUp]
    public void SetUp() => _handler = new PermissionAuthorizationHandler();

    private static ClaimsPrincipal Principal(params Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "TestAuth"));

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private async Task<bool> Evaluate(ClaimsPrincipal user, string permission)
    {
        var requirement = new PermissionRequirement(permission);
        var context = new AuthorizationHandlerContext([requirement], user, resource: null);
        await _handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    [Test]
    public async Task AnAdmin_IsGranted_EveryPermission()
    {
        var admin = Principal(new Claim(ClaimTypes.Role, "Admin"));

        foreach (var permission in EShopPermissions.All)
        {
            Assert.That(await Evaluate(admin, permission), Is.True,
                $"the Admin role must bundle '{permission}' — an admin that loses one silently gets 403 on the endpoints that need it");
        }
    }

    [Test]
    public async Task ARoleClaim_UnderTheShortName_IsAlsoHonoured()
    {
        // Whether roles arrive as ClaimTypes.Role or the short "role" name depends on
        // MapInboundClaims on the JWT handler, which differs between hosts.
        var admin = Principal(new Claim("role", "Admin"));

        Assert.That(await Evaluate(admin, EShopPermissions.CatalogWrite), Is.True);
    }

    [Test]
    public async Task ASignedInUser_WithNoRole_IsGrantedNothing()
    {
        var user = Principal(new Claim(ClaimTypes.NameIdentifier, "user-1"));

        foreach (var permission in EShopPermissions.All)
        {
            Assert.That(await Evaluate(user, permission), Is.False, permission);
        }
    }

    [Test]
    public async Task AnUnknownRole_GrantsNothing()
    {
        var user = Principal(new Claim(ClaimTypes.Role, "Manager"));

        Assert.That(await Evaluate(user, EShopPermissions.CatalogWrite), Is.False,
            "a role that exists in Identity but has no bundle here must grant nothing — failing closed is the safe direction");
    }

    [Test]
    public async Task AnAnonymousCaller_IsGrantedNothing()
    {
        Assert.That(await Evaluate(Anonymous(), EShopPermissions.CatalogWrite), Is.False);
    }

    [Test]
    public async Task ADirectPermissionClaim_IsGranted_WithoutAnyRole()
    {
        // Nothing issues this claim today; honouring it is what makes a future per-user grant an
        // Identity-only change rather than an every-endpoint change.
        var user = Principal(new Claim(EShopPermissions.ClaimType, EShopPermissions.PaymentsRefund));

        Assert.That(await Evaluate(user, EShopPermissions.PaymentsRefund), Is.True);
        Assert.That(await Evaluate(user, EShopPermissions.UsersManage), Is.False,
            "a direct grant must grant exactly the one permission it names");
    }

    [Test]
    public async Task PermissionMatching_IsCaseSensitive()
    {
        var user = Principal(new Claim(EShopPermissions.ClaimType, "Catalog.Write"));

        Assert.That(await Evaluate(user, EShopPermissions.CatalogWrite), Is.False,
            "a permission is an identifier, not prose — matching loosely would make the vocabulary's spelling unenforceable");
    }

    [Test]
    public void ARequirement_RefusesABlankPermission()
    {
        Assert.Throws<ArgumentException>(() => _ = new PermissionRequirement("  "));
    }
}
