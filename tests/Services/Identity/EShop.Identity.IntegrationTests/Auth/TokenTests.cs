using System.Net;
using System.Net.Http.Json;
using EShop.Identity.IntegrationTests.Models;
using FluentAssertions;

namespace EShop.Identity.IntegrationTests.Auth;

/// <summary>
/// Integration tests for Token Refresh and Revocation endpoints
/// </summary>
[TestFixture]
public class TokenTests : IntegrationTestBase
{
    private const string LoginEndpoint = "/api/v1/auth/login";
    private const string RefreshEndpoint = "/api/v1/auth/refresh-token";
    private const string RevokeEndpoint = "/api/v1/auth/revoke-token";

    private async Task<LoginResponse> LoginAsAdminAsync()
    {
        var loginRequest = new LoginRequest
        {
            Email = "admin@test.com",
            Password = "Admin@123456"
        };

        var response = await Client.PostAsJsonAsync(LoginEndpoint, loginRequest);
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    [Test]
    public async Task RefreshToken_WithValidToken_ShouldReturnNewTokens()
    {
        // Arrange
        var loginResult = await LoginAsAdminAsync();
        var request = new RefreshTokenRequest
        {
            RefreshToken = loginResult.RefreshToken
        };

        // Act
        var response = await Client.PostAsJsonAsync(RefreshEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<RefreshTokenResponse>();
        result.Should().NotBeNull();
        result!.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.ExpiresIn.Should().BeGreaterThan(0);
        
        // New refresh token should be different (rotation)
        result.RefreshToken.Should().NotBe(loginResult.RefreshToken);
    }

    [Test]
    public async Task RefreshToken_WithInvalidToken_ShouldReturnUnauthorized()
    {
        // Arrange
        var request = new RefreshTokenRequest
        {
            RefreshToken = "invalid-refresh-token"
        };

        // Act
        var response = await Client.PostAsJsonAsync(RefreshEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task RefreshToken_WithEmptyToken_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new RefreshTokenRequest
        {
            RefreshToken = ""
        };

        // Act
        var response = await Client.PostAsJsonAsync(RefreshEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task RefreshToken_WithRevokedToken_ShouldReturnUnauthorized()
    {
        // Arrange
        var loginResult = await LoginAsAdminAsync();
        
        // First, revoke the token
        var revokeRequest = new RevokeTokenRequest
        {
            RefreshToken = loginResult.RefreshToken
        };
        await Client.PostAsJsonAsync(RevokeEndpoint, revokeRequest);

        // Try to refresh with revoked token
        var refreshRequest = new RefreshTokenRequest
        {
            RefreshToken = loginResult.RefreshToken
        };

        // Act
        var response = await Client.PostAsJsonAsync(RefreshEndpoint, refreshRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task RefreshToken_OldTokenShouldBeInvalidAfterRotation()
    {
        // Arrange
        var loginResult = await LoginAsAdminAsync();
        var originalRefreshToken = loginResult.RefreshToken;

        // First refresh
        var firstRefreshRequest = new RefreshTokenRequest
        {
            RefreshToken = originalRefreshToken
        };
        var firstRefreshResponse = await Client.PostAsJsonAsync(RefreshEndpoint, firstRefreshRequest);
        firstRefreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Try to use original token again
        var secondRefreshRequest = new RefreshTokenRequest
        {
            RefreshToken = originalRefreshToken // Using old token
        };

        // Act
        var response = await Client.PostAsJsonAsync(RefreshEndpoint, secondRefreshRequest);

        // Assert - Old token should be invalid after rotation
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task RevokeToken_WithValidToken_ShouldSucceed()
    {
        // Arrange
        var loginResult = await LoginAsAdminAsync();
        var request = new RevokeTokenRequest
        {
            RefreshToken = loginResult.RefreshToken
        };

        // Act
        var response = await Client.PostAsJsonAsync(RevokeEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task RevokeToken_WithInvalidToken_ShouldStillSucceed()
    {
        // Arrange - Revoking non-existent token should not fail
        var request = new RevokeTokenRequest
        {
            RefreshToken = "non-existent-token"
        };

        // Act
        var response = await Client.PostAsJsonAsync(RevokeEndpoint, request);

        // Assert - Should succeed (idempotent)
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task RevokeToken_WithEmptyToken_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new RevokeTokenRequest
        {
            RefreshToken = ""
        };

        // Act
        var response = await Client.PostAsJsonAsync(RevokeEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task RefreshToken_MultipleRefreshes_ShouldAllSucceed()
    {
        // Arrange
        var loginResult = await LoginAsAdminAsync();
        var currentRefreshToken = loginResult.RefreshToken;

        // Act - Perform multiple refreshes in sequence
        for (int i = 0; i < 3; i++)
        {
            var request = new RefreshTokenRequest
            {
                RefreshToken = currentRefreshToken
            };

            var response = await Client.PostAsJsonAsync(RefreshEndpoint, request);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var result = await response.Content.ReadFromJsonAsync<RefreshTokenResponse>();
            result.Should().NotBeNull();
            result!.RefreshToken.Should().NotBe(currentRefreshToken);
            
            // Update for next iteration
            currentRefreshToken = result.RefreshToken;
        }

        // Assert - Final token should still be valid
        var finalRequest = new RefreshTokenRequest
        {
            RefreshToken = currentRefreshToken
        };
        var finalResponse = await Client.PostAsJsonAsync(RefreshEndpoint, finalRequest);
        finalResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
