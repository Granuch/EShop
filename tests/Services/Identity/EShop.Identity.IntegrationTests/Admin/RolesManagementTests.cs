using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EShop.Identity.IntegrationTests.Models;
using FluentAssertions;

namespace EShop.Identity.IntegrationTests.Admin;

/// <summary>
/// Integration tests for Roles Management endpoints (Admin only)
/// </summary>
[TestFixture]
public class RolesManagementTests : IntegrationTestBase
{
    private const string LoginEndpoint = "/api/v1/auth/login";
    private const string RolesEndpoint = "/api/v1/roles";

    private async Task<string> GetAdminTokenAsync()
    {
        var loginRequest = new LoginRequest { Email = "admin@test.com", Password = "Admin@123456" };
        var response = await Client.PostAsJsonAsync(LoginEndpoint, loginRequest);
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return result!.AccessToken;
    }

    private async Task<string> GetUserTokenAsync()
    {
        var loginRequest = new LoginRequest { Email = "user@test.com", Password = "User@123456" };
        var response = await Client.PostAsJsonAsync(LoginEndpoint, loginRequest);
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return result!.AccessToken;
    }

    private void SetAuthHeader(string token)
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [Test]
    public async Task GetRoles_WithAdminToken_ShouldReturnRoles()
    {
        // Arrange
        var token = await GetAdminTokenAsync();
        SetAuthHeader(token);

        // Act
        var response = await Client.GetAsync(RolesEndpoint);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Admin");
        content.Should().Contain("User");
    }

    [Test]
    public async Task GetRoles_WithUserToken_ShouldReturnForbidden()
    {
        // Arrange
        var token = await GetUserTokenAsync();
        SetAuthHeader(token);

        // Act
        var response = await Client.GetAsync(RolesEndpoint);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task GetRoles_WithoutToken_ShouldReturnUnauthorized()
    {
        // Arrange
        Client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await Client.GetAsync(RolesEndpoint);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task CreateRole_WithAdminToken_ShouldSucceed()
    {
        // Arrange
        var token = await GetAdminTokenAsync();
        SetAuthHeader(token);

        var request = new { Name = $"TestRole_{Guid.NewGuid()}", Description = "Test role" };

        // Act
        var response = await Client.PostAsJsonAsync(RolesEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Test]
    public async Task CreateRole_WithExistingName_ShouldReturnBadRequest()
    {
        // Arrange
        var token = await GetAdminTokenAsync();
        SetAuthHeader(token);

        var request = new { Name = "Admin", Description = "Duplicate role" };

        // Act
        var response = await Client.PostAsJsonAsync(RolesEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task CreateRole_WithUserToken_ShouldReturnForbidden()
    {
        // Arrange
        var token = await GetUserTokenAsync();
        SetAuthHeader(token);

        var request = new { Name = "UnauthorizedRole", Description = "Should not create" };

        // Act
        var response = await Client.PostAsJsonAsync(RolesEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task GetUsersInRole_WithAdminToken_ShouldReturnUsers()
    {
        // Arrange
        var token = await GetAdminTokenAsync();
        SetAuthHeader(token);

        // Act
        var response = await Client.GetAsync($"{RolesEndpoint}/Admin/users");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("admin@test.com");
    }

    [Test]
    public async Task GetUsersInRole_WithUserToken_ShouldReturnForbidden()
    {
        // Arrange
        var token = await GetUserTokenAsync();
        SetAuthHeader(token);

        // Act
        var response = await Client.GetAsync($"{RolesEndpoint}/Admin/users");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task DeleteRole_SystemRoles_ShouldReturnBadRequest()
    {
        // Arrange
        var token = await GetAdminTokenAsync();
        SetAuthHeader(token);

        // Act - Try to delete Admin role
        var response = await Client.DeleteAsync($"{RolesEndpoint}/Admin");

        // Assert - Should not allow deleting system roles
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.NotFound);
    }

    [Test]
    public async Task DeleteRole_NonExistentRole_ShouldReturnNotFound()
    {
        // Arrange
        var token = await GetAdminTokenAsync();
        SetAuthHeader(token);

        // Act
        var response = await Client.DeleteAsync($"{RolesEndpoint}/non-existent-id");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
