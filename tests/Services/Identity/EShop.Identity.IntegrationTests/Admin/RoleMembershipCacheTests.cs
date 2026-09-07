using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using EShop.Identity.IntegrationTests.Models;
using FluentAssertions;

namespace EShop.Identity.IntegrationTests.Admin;

/// <summary>
/// SEC-02. Role membership is read through ICachedUserRolesService (key "user_roles:{userId}",
/// 5-minute absolute TTL) when a token is minted, but RolesController mutated user_roles
/// directly and never invalidated that key — so a revoked role stayed in every freshly issued
/// token for up to five minutes after the revocation, and the grant direction was equally
/// invisible.
///
/// These are also the first tests of either membership endpoint: neither
/// POST nor DELETE /roles/{roleName}/users/{userId} had any coverage (TEST-06).
/// </summary>
[TestFixture]
public class RoleMembershipCacheTests : IntegrationTestBase
{
    private const string LoginEndpoint = "/api/v1/auth/login";
    private const string RolesEndpoint = "/api/v1/roles";

    private const string TargetEmail = "user@test.com";
    private const string TargetPassword = "User@123456";

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        // Login must go out unauthenticated — a stale bearer header from a previous admin call
        // would not break login, but keeping the two clients' headers separate keeps the
        // failure modes readable.
        var previous = Client.DefaultRequestHeaders.Authorization;
        Client.DefaultRequestHeaders.Authorization = null;

        try
        {
            var response = await Client.PostAsJsonAsync(LoginEndpoint, new LoginRequest { Email = email, Password = password });
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
        }
        finally
        {
            Client.DefaultRequestHeaders.Authorization = previous;
        }
    }

    private async Task AuthenticateAsAdminAsync()
    {
        var admin = await LoginAsync("admin@test.com", "Admin@123456");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
    }

    /// <summary>
    /// Reads roles out of the access token rather than off the login response body. The two do
    /// not come from the same place: LoginCommandHandler fills the response's user.roles from
    /// UserManager.GetRolesAsync directly (never cached), while TokenService builds the token's
    /// role claims through ICachedUserRolesService. Asserting on the response body therefore
    /// passes whether or not the cache is invalidated — it cannot see this bug at all.
    /// </summary>
    private static IEnumerable<string> RolesInToken(string accessToken) =>
        new JwtSecurityTokenHandler()
            .ReadJwtToken(accessToken)
            .Claims
            .Where(c => c.Type is ClaimTypes.Role or "role")
            .Select(c => c.Value);

    [Test]
    public async Task AddingAndRemovingARole_IsVisibleInTheNextTokenImmediately()
    {
        // Arrange — warm the roles cache for the target user by minting a token for them, then
        // create a role that is not one of the seeded ones.
        var before = await LoginAsync(TargetEmail, TargetPassword);
        RolesInToken(before.AccessToken).Should().NotContain("Auditor");

        await AuthenticateAsAdminAsync();

        var createResponse = await Client.PostAsJsonAsync(
            RolesEndpoint,
            new { Name = "Auditor", Description = "Read-only auditor" });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        var userId = before.User.Id;

        // Act — grant, then immediately mint a new token. Without invalidation this reads the
        // cache entry warmed above and the new role is missing.
        var addResponse = await Client.PostAsync($"{RolesEndpoint}/Auditor/users/{userId}", null);
        addResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterGrant = await LoginAsync(TargetEmail, TargetPassword);

        // Assert
        RolesInToken(afterGrant.AccessToken).Should().Contain("Auditor",
            "a granted role must be in the next token, not up to five minutes later");

        // Act — revoke, then immediately mint again. This is the security-critical direction.
        await AuthenticateAsAdminAsync();
        var removeResponse = await Client.DeleteAsync($"{RolesEndpoint}/Auditor/users/{userId}");
        removeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterRevoke = await LoginAsync(TargetEmail, TargetPassword);

        // Assert
        RolesInToken(afterRevoke.AccessToken).Should().NotContain("Auditor",
            "a revoked role must not survive in a token minted after the revocation");
    }

    [Test]
    public async Task RemoveUserFromRole_WithUnknownRole_ShouldReturnNotFound()
    {
        // RemoveUserFromRole had no RoleExistsAsync check, so a misspelled role name fell
        // through to RemoveFromRoleAsync and surfaced as 400 rather than 404 — asymmetric with
        // AddUserToRole, which has always checked.
        var target = await LoginAsync(TargetEmail, TargetPassword);
        await AuthenticateAsAdminAsync();

        var response = await Client.DeleteAsync($"{RolesEndpoint}/NoSuchRole/users/{target.User!.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task AddUserToRole_WithUnknownUser_ShouldReturnNotFound()
    {
        await AuthenticateAsAdminAsync();

        var response = await Client.PostAsync($"{RolesEndpoint}/Admin/users/{Guid.NewGuid()}", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
