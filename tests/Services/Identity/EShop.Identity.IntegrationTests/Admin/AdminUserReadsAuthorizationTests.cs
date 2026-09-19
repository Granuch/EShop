using System.Net;
using EShop.BuildingBlocks.Infrastructure.Authorization;
using EShop.Identity.IntegrationTests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EShop.Identity.IntegrationTests.Admin;

/// <summary>
/// Admin panel S6 — who may reach the admin user screens. Signs in as a <b>regular user</b>, which
/// is the only way to tell "requires the right permission" from "requires any signed-in caller":
/// an admin passes both forms and an anonymous caller fails both.
/// </summary>
/// <remarks>
/// This is Identity's counterpart to Catalog's <c>NonAdminAuthorizationTests</c>, and it pairs the
/// behavioural 403s with a structural check for the same reason — each catches what the other
/// cannot. Downgrading one action's policy turns only the behavioural half red; redefining the
/// permission policy as <c>RequireAuthenticatedUser()</c> turns only that half red while the
/// structural half stays green.
/// </remarks>
[TestFixture]
[Category("Integration")]
public class AdminUserReadsAuthorizationTests : AuthenticatedIntegrationTestBase
{
    private const string Endpoint = "/api/v1/admin/users";

    protected override string TestUserEmail => TestUsers.RegularUser.Email;
    protected override string TestUserPassword => TestUsers.RegularUser.Password;

    private static readonly string[] AdminOnlyPaths =
    [
        Endpoint,
        $"{Endpoint}/stats",
        $"{Endpoint}/some-user-id",
        $"{Endpoint}/some-user-id/roles",
        $"{Endpoint}/some-user-id/sessions"
    ];

    [TestCaseSource(nameof(AdminOnlyPaths))]
    public async Task ASignedInNonAdmin_IsForbidden(string path)
    {
        var response = await Client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            $"{path} must require the {EShopPermissions.UsersRead} permission, not merely a signed-in caller; "
            + $"body: {await response.Content.ReadAsStringAsync()}");
    }

    [TestCaseSource(nameof(AdminOnlyPaths))]
    public async Task AnAnonymousCaller_IsUnauthorized(string path)
    {
        // A second client from the factory, not Client with the header nulled: HttpClient merges
        // DefaultRequestHeaders into any request that lacks them, so nulling the header on a
        // request leaves the bearer token in place and the test asserts the authenticated view.
        using var anonymous = Factory.CreateClient();

        var response = await anonymous.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Every <b>read</b> action carries <c>users.read</c> and is listed above.
    /// </summary>
    /// <remarks>
    /// S7 narrowed this from "every action on the controller" to "every GET". The controller gained
    /// thirteen write actions, so the count assertion below went red — which is the test doing its
    /// job, not a regression. The writes get the same treatment in
    /// <c>AdminUserWritesAuthorizationTests</c>, where the policy they must additionally carry is
    /// the thing worth asserting. Splitting on the verb keeps each half able to say something
    /// specific; one combined list could only assert the class-level policy they share.
    /// </remarks>
    [Test]
    public void EveryAdminUserRead_CarriesTheUsersReadPolicy_AndIsListedHere()
    {
        var endpoints = AdminUserEndpoints.Of(Factory.Services)
            .Where(AdminUserEndpoints.IsRead)
            .ToList();

        endpoints.Should().NotBeEmpty("the controller must be mapped at all");

        foreach (var endpoint in endpoints)
        {
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Should().Contain(a => a.Policy == EShopPermissions.UsersRead,
                    $"{endpoint.RoutePattern.RawText} must carry the {EShopPermissions.UsersRead} policy");

            endpoint.Metadata.GetMetadata<IAllowAnonymous>()
                .Should().BeNull($"{endpoint.RoutePattern.RawText} must not be anonymous");
        }

        // A new read action added to the controller without a line in AdminOnlyPaths above would
        // ship untested behaviourally, so the counts are compared.
        endpoints.Should().HaveCount(AdminOnlyPaths.Length,
            "a GET action added to AdminUsersController must also be added to AdminOnlyPaths");
    }

    [Test]
    public async Task ANonAdmin_CannotReadAnotherUsersSessions()
    {
        // The worst thing this controller could leak, asserted from the caller most likely to try.
        var response = await Client.GetAsync($"{Endpoint}/{Guid.NewGuid()}/sessions");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("tokenHash");
    }
}
