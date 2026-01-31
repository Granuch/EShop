using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EShop.Identity.IntegrationTests.Models;
using FluentAssertions;

namespace EShop.Identity.IntegrationTests.Account;

/// <summary>
/// Integration tests for Password Change endpoint
/// </summary>
[TestFixture]
public class ChangePasswordTests : IntegrationTestBase
{
    private const string LoginEndpoint = "/api/v1/auth/login";
    private const string RefreshEndpoint = "/api/v1/auth/refresh-token";
    private const string ChangePasswordEndpoint = "/api/v1/account/change-password";

    private async Task<(string AccessToken, string RefreshToken)> LoginAsync(string email, string password)
    {
        var loginRequest = new LoginRequest { Email = email, Password = password };
        var response = await Client.PostAsJsonAsync(LoginEndpoint, loginRequest);
        
        if (!response.IsSuccessStatusCode)
        {
            var content = await response.Content.ReadAsStringAsync();
            throw new Exception($"Login failed: {response.StatusCode}, {content}");
        }
        
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return (result!.AccessToken, result.RefreshToken);
    }

    private void SetAuthHeader(string token)
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [Test]
    public async Task ChangePassword_WithValidData_ShouldSucceed()
    {
        // Arrange - use seeded user
        var email = "user@test.com";
        var oldPassword = "User@123456";
        var newPassword = "NewUser@123456";
        
        var (accessToken, _) = await LoginAsync(email, oldPassword);
        SetAuthHeader(accessToken);

        var request = new ChangePasswordRequest
        {
            CurrentPassword = oldPassword,
            NewPassword = newPassword
        };

        // Act
        var response = await Client.PostAsJsonAsync(ChangePasswordEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify can login with new password
        Client.DefaultRequestHeaders.Authorization = null;
        var loginRequest = new LoginRequest { Email = email, Password = newPassword };
        var loginResponse = await Client.PostAsJsonAsync(LoginEndpoint, loginRequest);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        
        // Restore password for other tests
        var (newAccessToken, _) = await LoginAsync(email, newPassword);
        SetAuthHeader(newAccessToken);
        var restoreRequest = new ChangePasswordRequest
        {
            CurrentPassword = newPassword,
            NewPassword = oldPassword
        };
        await Client.PostAsJsonAsync(ChangePasswordEndpoint, restoreRequest);
    }

    [Test]
    public async Task ChangePassword_ShouldInvalidateRefreshTokens()
    {
        // Arrange - use seeded admin user
        var email = "admin@test.com";
        var oldPassword = "Admin@123456";
        var newPassword = "NewAdmin@123456";
        
        var (accessToken, refreshToken) = await LoginAsync(email, oldPassword);
        SetAuthHeader(accessToken);

        var changeRequest = new ChangePasswordRequest
        {
            CurrentPassword = oldPassword,
            NewPassword = newPassword
        };

        // Act - Change password
        var changeResponse = await Client.PostAsJsonAsync(ChangePasswordEndpoint, changeRequest);
        changeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Try to use old refresh token
        var refreshRequest = new RefreshTokenRequest
        {
            RefreshToken = refreshToken
        };
        var refreshResponse = await Client.PostAsJsonAsync(RefreshEndpoint, refreshRequest);

        // Assert - Refresh token should be invalid
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        
        // Restore password for other tests
        var (newAccessToken, _) = await LoginAsync(email, newPassword);
        SetAuthHeader(newAccessToken);
        var restoreRequest = new ChangePasswordRequest
        {
            CurrentPassword = newPassword,
            NewPassword = oldPassword
        };
        await Client.PostAsJsonAsync(ChangePasswordEndpoint, restoreRequest);
    }

    [Test]
    public async Task ChangePassword_WithWrongCurrentPassword_ShouldReturnBadRequest()
    {
        // Arrange
        var (accessToken, _) = await LoginAsync("user@test.com", "User@123456");
        SetAuthHeader(accessToken);

        var request = new ChangePasswordRequest
        {
            CurrentPassword = "WrongPass@123",
            NewPassword = "NewPass@123"
        };

        // Act
        var response = await Client.PostAsJsonAsync(ChangePasswordEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ChangePassword_WithEmptyCurrentPassword_ShouldReturnBadRequest()
    {
        // Arrange
        var (accessToken, _) = await LoginAsync("user@test.com", "User@123456");
        SetAuthHeader(accessToken);

        var request = new ChangePasswordRequest
        {
            CurrentPassword = "",
            NewPassword = "NewPass@123"
        };

        // Act
        var response = await Client.PostAsJsonAsync(ChangePasswordEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ChangePassword_WithEmptyNewPassword_ShouldReturnBadRequest()
    {
        // Arrange
        var (accessToken, _) = await LoginAsync("user@test.com", "User@123456");
        SetAuthHeader(accessToken);

        var request = new ChangePasswordRequest
        {
            CurrentPassword = "User@123456",
            NewPassword = ""
        };

        // Act
        var response = await Client.PostAsJsonAsync(ChangePasswordEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ChangePassword_WithoutToken_ShouldReturnUnauthorized()
    {
        // Arrange
        Client.DefaultRequestHeaders.Authorization = null;

        var request = new ChangePasswordRequest
        {
            CurrentPassword = "OldPass@123",
            NewPassword = "NewPass@123"
        };

        // Act
        var response = await Client.PostAsJsonAsync(ChangePasswordEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task ChangePassword_WithWeakNewPassword_ShouldReturnBadRequest()
    {
        // Arrange
        var (accessToken, _) = await LoginAsync("user@test.com", "User@123456");
        SetAuthHeader(accessToken);

        var request = new ChangePasswordRequest
        {
            CurrentPassword = "User@123456",
            NewPassword = "weak" // Too weak
        };

        // Act
        var response = await Client.PostAsJsonAsync(ChangePasswordEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
