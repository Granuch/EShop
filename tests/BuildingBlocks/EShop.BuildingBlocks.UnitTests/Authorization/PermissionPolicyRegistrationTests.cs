using EShop.BuildingBlocks.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.BuildingBlocks.UnitTests.Authorization;

/// <summary>
/// Pins the registration half of the permission model. A permission string is also its policy name,
/// so the failure mode when the two drift is a runtime
/// <c>InvalidOperationException: The AuthorizationPolicy named '…' was not found</c> on the first
/// request to that endpoint — loud, but only in production if nothing checks it here.
/// </summary>
[TestFixture]
public class PermissionPolicyRegistrationTests
{
    private ServiceProvider _provider = null!;

    [SetUp]
    public void SetUp()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEShopPermissions();
        _provider = services.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown() => _provider.Dispose();

    [Test]
    public async Task EveryPermission_HasARegisteredPolicy()
    {
        var policies = _provider.GetRequiredService<IAuthorizationPolicyProvider>();

        foreach (var permission in EShopPermissions.All)
        {
            var policy = await policies.GetPolicyAsync(permission);

            Assert.That(policy, Is.Not.Null, $"no policy registered for '{permission}'");
            Assert.That(
                policy!.Requirements.OfType<PermissionRequirement>().Select(r => r.Permission),
                Does.Contain(permission));
        }
    }

    [Test]
    public void TheHandler_IsRegistered()
    {
        // Registering the policies without the handler is the silent-failure shape: every
        // permission policy becomes unsatisfiable and every admin endpoint answers 403.
        Assert.That(
            _provider.GetServices<IAuthorizationHandler>().OfType<PermissionAuthorizationHandler>().Count(),
            Is.EqualTo(1));
    }

    [Test]
    public void TheHandler_IsASingleton_AndSoMustHoldNoScopedState()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEShopPermissions();

        var descriptor = services.Single(d =>
            d.ServiceType == typeof(IAuthorizationHandler)
            && d.ImplementationType == typeof(PermissionAuthorizationHandler));

        Assert.That(descriptor.Lifetime, Is.EqualTo(ServiceLifetime.Singleton),
            "Ordering audit H1 was a singleton authorization handler capturing a scoped DbContext; "
            + "this one stays a singleton only because it reads nothing but the principal and a static map");
    }

    [Test]
    public void ThePermissionVocabulary_HasNoDuplicates()
    {
        Assert.That(EShopPermissions.All, Is.Unique);
    }

    [Test]
    public void TheAdminBundle_CoversTheWholeVocabulary()
    {
        // The invariant the whole Q4c migration rests on, asserted against the map directly as well
        // as through the handler, so a bundle change is caught even if the handler changes shape.
        Assert.That(
            RolePermissionBundles.PermissionsFor(RolePermissionBundles.AdminRole),
            Is.EquivalentTo(EShopPermissions.All));
    }
}
