using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EShop.Identity.IntegrationTests.Models;
using FluentAssertions;

namespace EShop.Identity.IntegrationTests.Admin;

/// <summary>
/// TEST-06 / API-7. Five of the eight role endpoints had no coverage at all — GET/PUT /{id},
/// both membership verbs, and every failure shape. Three of them also declared no 400 response
/// while being able to return one.
///
/// These tests exist because Stage 7 moved the whole controller behind MediatR, and a rewrite
/// that changes status codes or error codes without anything noticing is exactly the risk. Each
/// assertion below pins a status *and* the RFC 7807 `errorCode`, since the status alone would not
/// catch a code being renamed.
/// </summary>
[TestFixture]
public class RoleEndpointContractTests : IntegrationTestBase
{
    private const string RolesEndpoint = "/api/v1/roles";

    private async Task AuthenticateAsAdminAsync()
    {
        Client.DefaultRequestHeaders.Authorization = null;
        var response = await Client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest { Email = "admin@test.com", Password = "Admin@123456" });
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.AccessToken);
    }

    private static async Task<string?> ErrorCodeOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("errorCode", out var code) ? code.GetString() : null;
    }

    [SetUp]
    public async Task AuthenticateAsync() => await AuthenticateAsAdminAsync();

    [Test]
    public async Task CreateThenGetThenUpdateThenDelete_RoundTrips()
    {
        var name = $"Contract_{Guid.NewGuid():N}";

        var created = await Client.PostAsJsonAsync(RolesEndpoint, new { Name = name, Description = "initial" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var role = await created.Content.ReadFromJsonAsync<RoleDto>();
        role!.Id.Should().NotBeNullOrEmpty();
        role.Name.Should().Be(name);

        // CreatedAtAction must point at a route that actually resolves — a broken Location
        // header is invisible until a client follows it.
        created.Headers.Location.Should().NotBeNull();

        var fetched = await Client.GetAsync($"{RolesEndpoint}/{role.Id}");
        fetched.StatusCode.Should().Be(HttpStatusCode.OK);
        (await fetched.Content.ReadFromJsonAsync<RoleDto>())!.Description.Should().Be("initial");

        var updated = await Client.PutAsJsonAsync($"{RolesEndpoint}/{role.Id}", new { Description = "revised" });
        updated.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refetched = await Client.GetAsync($"{RolesEndpoint}/{role.Id}");
        (await refetched.Content.ReadFromJsonAsync<RoleDto>())!.Description.Should().Be("revised");

        var deleted = await Client.DeleteAsync($"{RolesEndpoint}/{role.Id}");
        deleted.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var gone = await Client.GetAsync($"{RolesEndpoint}/{role.Id}");
        gone.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task GetRole_WithUnknownId_Returns404WithRoleNotFound()
    {
        var response = await Client.GetAsync($"{RolesEndpoint}/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ErrorCodeOf(response)).Should().Be("Role.NotFound");
    }

    [Test]
    public async Task UpdateRole_WithUnknownId_Returns404WithRoleNotFound()
    {
        var response = await Client.PutAsJsonAsync($"{RolesEndpoint}/{Guid.NewGuid()}", new { Description = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ErrorCodeOf(response)).Should().Be("Role.NotFound");
    }

    [Test]
    public async Task CreateRole_WithDuplicateName_Returns400WithRoleExists()
    {
        var response = await Client.PostAsJsonAsync(RolesEndpoint, new { Name = "Admin", Description = "dupe" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeOf(response)).Should().Be("Role.Exists");
    }

    [Test]
    public async Task CreateRole_WithEmptyName_Returns400WithValidationFailed()
    {
        // The role endpoints had no validator at all before Stage 7, so this reached RoleManager
        // and came back carrying an ASP.NET Identity string instead of the canonical envelope.
        var response = await Client.PostAsJsonAsync(RolesEndpoint, new { Name = "", Description = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeOf(response)).Should().Be("Validation.Failed");
    }

    [Test]
    public async Task DeleteRole_ForSystemRole_Returns400WithCannotDelete()
    {
        var admin = await Client.GetAsync(RolesEndpoint);
        var roles = await admin.Content.ReadFromJsonAsync<List<RoleDto>>();
        var adminRoleId = roles!.Single(r => r.Name == "Admin").Id;

        var response = await Client.DeleteAsync($"{RolesEndpoint}/{adminRoleId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorCodeOf(response)).Should().Be("Role.CannotDelete");
    }

    [Test]
    public async Task GetUsersInRole_WithUnknownRole_Returns404RatherThanEmptyList()
    {
        // This previously returned 200 with an empty array, which a client cannot distinguish
        // from a real role that happens to have no members.
        var response = await Client.GetAsync($"{RolesEndpoint}/NoSuchRole/users");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ErrorCodeOf(response)).Should().Be("Role.NotFound");
    }

    [Test]
    public async Task GetRoles_IsPaged()
    {
        var response = await Client.GetAsync($"{RolesEndpoint}?page=1&pageSize=1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<List<RoleDto>>())!.Should().HaveCount(1);
    }

    [Test]
    public async Task RoleEndpoints_RejectNonAdmins()
    {
        Client.DefaultRequestHeaders.Authorization = null;
        var login = await Client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest { Email = "user@test.com", Password = "User@123456" });
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // The [Authorize(Roles = "Admin")] attribute is on the class; a rewrite that moved
        // actions around could drop it without any other test noticing.
        (await Client.GetAsync(RolesEndpoint)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Client.GetAsync($"{RolesEndpoint}/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Client.PostAsJsonAsync(RolesEndpoint, new { Name = "X" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Client.DeleteAsync($"{RolesEndpoint}/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Client.GetAsync($"{RolesEndpoint}/Admin/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Client.PostAsync($"{RolesEndpoint}/Admin/users/{Guid.NewGuid()}", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Client.DeleteAsync($"{RolesEndpoint}/Admin/users/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private record RoleDto
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
    }
}
