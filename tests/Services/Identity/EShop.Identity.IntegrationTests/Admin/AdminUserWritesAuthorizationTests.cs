using System.Net;
using System.Net.Http.Json;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.Identity.IntegrationTests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace EShop.Identity.IntegrationTests.Admin;

/// <summary>
/// Admin panel S7 — who may reach the admin user <b>writes</b>. Signs in as a regular user, the
/// only caller that can tell "requires users.manage" from "requires any signed-in caller".
/// </summary>
/// <remarks>
/// <para>
/// Paired behavioural and structural checks — but <b>not</b> in the symmetric way the S6 reads
/// fixture describes, and falsification is what showed the difference. Removing one action's
/// <c>users.manage</c> attribute turns <b>only the structural half</b> red: the class-level
/// <c>users.read</c> still applies, and the seeded non-admin holds neither permission, so the 403
/// is unchanged. The behavioural half only moves when <i>both</i> policies come off an action —
/// the repo's "two redundant lines" shape, verified by a round that removed each and then both.
/// </para>
/// <para>
/// So the structural half is the load-bearing one here, and that is fitting: fourteen
/// near-identical actions are exactly the shape where one gets added without its attribute. It
/// fails the build on a write endpoint carrying no manage-level policy <b>and</b> on one missing
/// from the list below. The behavioural half is what catches the other direction — a permission
/// policy redefined as <c>RequireAuthenticatedUser()</c> leaves every attribute in place and every
/// structural assertion green.
/// </para>
/// </remarks>
[TestFixture]
[Category("Integration")]
public class AdminUserWritesAuthorizationTests : AuthenticatedIntegrationTestBase
{
    private const string Endpoint = "/api/v1/admin/users";

    protected override string TestUserEmail => TestUsers.RegularUser.Email;
    protected override string TestUserPassword => TestUsers.RegularUser.Password;

    /// <summary>
    /// Method, path, and the manage-level policy the action must carry on top of the class-level
    /// <c>users.read</c>.
    /// </summary>
    private static readonly object[] WriteRoutes =
    [
        new object[] { "POST", "", EShopPermissions.UsersManage },
        new object[] { "PUT", "/some-user-id", EShopPermissions.UsersManage },
        new object[] { "PUT", "/some-user-id/email", EShopPermissions.UsersManage },
        new object[] { "POST", "/some-user-id/activate", EShopPermissions.UsersManage },
        new object[] { "POST", "/some-user-id/deactivate", EShopPermissions.UsersManage },
        new object[] { "DELETE", "/some-user-id", EShopPermissions.UsersManage },
        new object[] { "POST", "/some-user-id/restore", EShopPermissions.UsersManage },
        new object[] { "POST", "/some-user-id/lock", EShopPermissions.UsersManage },
        new object[] { "POST", "/some-user-id/unlock", EShopPermissions.UsersManage },
        new object[] { "POST", "/some-user-id/reset-password", EShopPermissions.UsersManage },
        new object[] { "POST", "/some-user-id/confirm-email", EShopPermissions.UsersManage },
        new object[] { "POST", "/some-user-id/disable-2fa", EShopPermissions.UsersManage },
        new object[] { "PUT", "/some-user-id/roles", EShopPermissions.RolesManage },
        new object[] { "POST", "/some-user-id/revoke-tokens", EShopPermissions.UsersManage }
    ];

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string suffix)
    {
        var path = $"{Endpoint}{suffix}";

        // An empty body is enough: authorization runs before model binding and validation, so a
        // 403 here cannot be a validation failure wearing a disguise.
        return method switch
        {
            "PUT" => client.PutAsJsonAsync(path, new { }),
            "DELETE" => client.DeleteAsync(path),
            _ => client.PostAsJsonAsync(path, new { })
        };
    }

    [TestCaseSource(nameof(WriteRoutes))]
    public async Task ASignedInNonAdmin_IsForbidden(string method, string suffix, string permission)
    {
        var response = await SendAsync(Client, method, suffix);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"{method} {Endpoint}{suffix} must require {permission}, not merely a signed-in caller; "
            + $"body: {await response.Content.ReadAsStringAsync()}");
    }

    [TestCaseSource(nameof(WriteRoutes))]
    public async Task AnAnonymousCaller_IsUnauthorized(string method, string suffix, string permission)
    {
        // A second client from the factory, not Client with the header nulled: HttpClient merges
        // DefaultRequestHeaders into a request that lacks them, so nulling leaves the token in place
        // and the test silently asserts the authenticated view.
        using var anonymous = Factory.CreateClient();

        var response = await SendAsync(anonymous, method, suffix);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            $"{method} {Endpoint}{suffix} ({permission})");
    }

    [Test]
    public void EveryWriteEndpoint_CarriesAManagePolicy_AndIsListedHere()
    {
        var endpoints = AdminUserEndpoints.Of(Factory.Services)
            .Where(AdminUserEndpoints.IsWrite)
            .ToList();

        endpoints.Should().NotBeEmpty();

        foreach (var endpoint in endpoints)
        {
            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Select(a => a.Policy)
                .ToList();

            policies.Should().Contain(EShopPermissions.UsersRead,
                $"{AdminUserEndpoints.Describe(endpoint)} inherits the controller's policy, and losing it "
                + "would mean the class-level attribute had been removed");

            policies.Should().ContainSingle(p =>
                    p == EShopPermissions.UsersManage || p == EShopPermissions.RolesManage,
                $"{AdminUserEndpoints.Describe(endpoint)} must add exactly one manage-level permission — "
                + "a write guarded only by users.read is readable by anyone who may look at the list");

            endpoint.Metadata.GetMetadata<IAllowAnonymous>()
                .Should().BeNull($"{AdminUserEndpoints.Describe(endpoint)} must not be anonymous");
        }

        endpoints.Should().HaveCount(WriteRoutes.Length,
            "a write action added to AdminUsersController must also be added to WriteRoutes, or it ships "
            + "with no behavioural authorization test at all");
    }

    /// <summary>
    /// The role editor is the one action guarded by <c>roles.manage</c>, which is what the
    /// vocabulary defines as "change who is in them". Pinned separately so a refactor that
    /// normalised every write onto <c>users.manage</c> has to be a deliberate decision.
    /// </summary>
    [Test]
    public void TheRoleEditor_IsGuardedByRolesManage_NotUsersManage()
    {
        var endpoint = AdminUserEndpoints.Of(Factory.Services)
            .Where(AdminUserEndpoints.IsWrite)
            .Single(e => e.RoutePattern.RawText?.EndsWith("/roles", StringComparison.OrdinalIgnoreCase) == true);

        var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(a => a.Policy).ToList();

        policies.Should().Contain(EShopPermissions.RolesManage);
        policies.Should().NotContain(EShopPermissions.UsersManage);
    }
}
