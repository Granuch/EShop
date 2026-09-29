using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.Identity.Infrastructure.Services;

namespace EShop.Identity.UnitTests.Services;

/// <summary>
/// Frontend-contracts F-07. The permissions Identity reports must be the permissions the services
/// grant, so the resolver has to read <see cref="RolePermissionBundles"/> the way
/// <c>PermissionAuthorizationHandler</c> does: case-insensitively by role, nothing for a role with no
/// bundle.
/// </summary>
[TestFixture]
public class RolePermissionResolverTests
{
    private readonly RolePermissionResolver _resolver = new();

    [Test]
    public void Admin_GrantsEveryPermission_InTheVocabularysOrder()
    {
        Assert.That(_resolver.PermissionsFor(["Admin"]), Is.EqualTo(EShopPermissions.All));
    }

    [Test]
    public void RoleNames_MatchInAnyCase_AsTheAuthorizationHandlerMatchesThem()
    {
        Assert.That(_resolver.PermissionsFor(["admin"]), Is.EqualTo(EShopPermissions.All));
    }

    [Test]
    public void ARoleWithNoBundle_GrantsNothing()
    {
        Assert.That(_resolver.PermissionsFor(["User", "Customer"]), Is.Empty);
        Assert.That(_resolver.PermissionsFor([]), Is.Empty);
    }

    [Test]
    public void EachPermission_IsListedOnce_WhateverGrantsIt()
    {
        var permissions = _resolver.PermissionsFor(["Admin", "User", "ADMIN"]);

        Assert.That(permissions, Is.EqualTo(EShopPermissions.All));
        Assert.That(permissions, Is.Unique);
    }
}
