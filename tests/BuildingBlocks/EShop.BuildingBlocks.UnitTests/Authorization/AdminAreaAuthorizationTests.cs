using System.Security.Claims;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.BuildingBlocks.UnitTests.Authorization;

/// <summary>
/// frontend-contracts R6 (F-08, F-45). <c>AdminArea</c> is the gateway's admin gate: "holds at least one EShop
/// permission", by a <c>permission</c> claim or a role bundle. It must admit every operator a service could admit and
/// nobody else, because it replaced <c>RequireRole("Admin")</c>, which refused a permission-only operator.
/// </summary>
[TestFixture]
public class AdminAreaAuthorizationTests
{
    private AdminAreaAuthorizationHandler _handler = null!;

    [SetUp]
    public void SetUp() => _handler = new AdminAreaAuthorizationHandler();

    private static ClaimsPrincipal Principal(params Claim[] claims)
        => new(new ClaimsIdentity(claims, authenticationType: "TestAuth"));

    private async Task<bool> Evaluate(ClaimsPrincipal user)
    {
        var context = new AuthorizationHandlerContext([new AdminAreaRequirement()], user, resource: null);
        await _handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    [Test]
    public async Task AnAdmin_IsAdmitted()
    {
        Assert.That(await Evaluate(Principal(new Claim(ClaimTypes.Role, "Admin"))), Is.True);
    }

    [Test]
    public async Task ARoleClaim_UnderTheShortName_IsAlsoHonoured()
    {
        Assert.That(await Evaluate(Principal(new Claim("role", "Admin"))), Is.True);
    }

    [Test]
    public async Task EveryPermission_OnItsOwn_IsEnough()
    {
        // The F-08 case: an operator holding one permission and no Admin role. Each permission alone must open the
        // gate, or the service behind it could never be reached by an operator it would have admitted.
        foreach (var permission in EShopPermissions.All)
        {
            var user = Principal(new Claim(EShopPermissions.ClaimType, permission));

            Assert.That(await Evaluate(user), Is.True, permission);
        }
    }

    [Test]
    public async Task ACustomer_IsRefused()
    {
        Assert.That(await Evaluate(Principal(new Claim(ClaimTypes.NameIdentifier, "user-1"))), Is.False);
        Assert.That(await Evaluate(Principal(new Claim(ClaimTypes.Role, "User"))), Is.False);
    }

    [Test]
    public async Task AnUnknownRole_IsRefused()
    {
        Assert.That(await Evaluate(Principal(new Claim(ClaimTypes.Role, "Manager"))), Is.False,
            "a role with no bundle grants no permission, so it is not an operator");
    }

    [TestCase("orders.everything")]
    [TestCase("Orders.Read")]
    [TestCase("")]
    public async Task APermissionClaimOutsideTheVocabulary_IsRefused(string claim)
    {
        Assert.That(await Evaluate(Principal(new Claim(EShopPermissions.ClaimType, claim))), Is.False);
    }

    [Test]
    public async Task AnAnonymousCaller_IsRefused_EvenWithAnAdminRoleClaim()
    {
        // An identity with no authentication type is anonymous, whatever claims it carries.
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")]));

        Assert.That(await Evaluate(anonymous), Is.False);
    }

    [Test]
    public async Task ThePolicy_IsRegistered_WithItsHandler()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEShopPermissions();
        await using var provider = services.BuildServiceProvider();

        var policy = await provider.GetRequiredService<IAuthorizationPolicyProvider>()
            .GetPolicyAsync(AdminAreaRequirement.PolicyName);

        Assert.That(policy, Is.Not.Null, "the gateway names this policy on every admin route");
        Assert.That(policy!.Requirements.OfType<AdminAreaRequirement>().Count(), Is.EqualTo(1));
        Assert.That(provider.GetServices<IAuthorizationHandler>().OfType<AdminAreaAuthorizationHandler>().Count(),
            Is.EqualTo(1), "a policy with no handler for its requirement refuses everyone");
    }
}
