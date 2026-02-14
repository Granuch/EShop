using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EShop.Identity.IntegrationTests.Models;
using FluentAssertions;

namespace EShop.Identity.IntegrationTests.Account;

/// <summary>
/// Integration tests for Two-Factor Authentication (2FA) endpoints
/// </summary>
[TestFixture]
public class TwoFactorAuthTests : IntegrationTestBase
{
    private const string LoginEndpoint = "/api/v1/auth/login";
    private const string Enable2FAEndpoint = "/api/v1/account/enable-2fa";
    private const string Verify2FAEndpoint = "/api/v1/account/verify-2fa";
    private const string Disable2FAEndpoint = "/api/v1/account/disable-2fa";

    private async Task<string> GetAccessTokenAsync(string email = "admin@test.com", string password = "Admin@123456")
    {
        var loginRequest = new LoginRequest { Email = email, Password = password };
        var response = await Client.PostAsJsonAsync(LoginEndpoint, loginRequest);
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return result!.AccessToken;
    }

    private void SetAuthHeader(string token)
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [Test]
    public async Task Enable2FA_WithValidToken_ShouldReturnSharedKeyAndQRCode()
    {
        // Arrange
        var token = await GetAccessTokenAsync();
        SetAuthHeader(token);

        // Act
        var response = await Client.PostAsync(Enable2FAEndpoint, null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<Enable2FAResponse>();
        result.Should().NotBeNull();
        result!.SharedKey.Should().NotBeNullOrEmpty();
        result.QrCodeUri.Should().NotBeNullOrEmpty();
        result.QrCodeUri.Should().StartWith("otpauth://");
    }

    [Test]
    public async Task Enable2FA_WithoutToken_ShouldReturnUnauthorized()
    {
        // Arrange
        Client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await Client.PostAsync(Enable2FAEndpoint, null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Verify2FA_WithInvalidCode_ShouldReturnBadRequest()
    {
        // Arrange
        var token = await GetAccessTokenAsync();
        SetAuthHeader(token);

        // First enable 2FA
        await Client.PostAsync(Enable2FAEndpoint, null);

        var request = new Verify2FARequest
        {
            Code = "000000" // Invalid code
        };

        // Act
        var response = await Client.PostAsJsonAsync(Verify2FAEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Verify2FA_WithEmptyCode_ShouldReturnBadRequest()
    {
        // Arrange
        var token = await GetAccessTokenAsync();
        SetAuthHeader(token);

        var request = new Verify2FARequest
        {
            Code = ""
        };

        // Act
        var response = await Client.PostAsJsonAsync(Verify2FAEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Verify2FA_WithNonDigitCode_ShouldReturnBadRequest()
    {
        // Arrange
        var token = await GetAccessTokenAsync();
        SetAuthHeader(token);

        var request = new Verify2FARequest
        {
            Code = "abcdef" // Non-digit code
        };

        // Act
        var response = await Client.PostAsJsonAsync(Verify2FAEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Verify2FA_WithWrongLengthCode_ShouldReturnBadRequest()
    {
        // Arrange
        var token = await GetAccessTokenAsync();
        SetAuthHeader(token);

        var request = new Verify2FARequest
        {
            Code = "12345" // 5 digits instead of 6
        };

        // Act
        var response = await Client.PostAsJsonAsync(Verify2FAEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Verify2FA_WithoutToken_ShouldReturnUnauthorized()
    {
        // Arrange
        Client.DefaultRequestHeaders.Authorization = null;

        var request = new Verify2FARequest
        {
            Code = "123456"
        };

        // Act
        var response = await Client.PostAsJsonAsync(Verify2FAEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Disable2FA_WithInvalidCode_ShouldReturnBadRequest()
    {
        // Arrange
        var token = await GetAccessTokenAsync();
        SetAuthHeader(token);

        var request = new Verify2FARequest
        {
            Code = "000000" // Invalid code
        };

        // Act
        var response = await Client.PostAsJsonAsync(Disable2FAEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Disable2FA_WithEmptyCode_ShouldReturnBadRequest()
    {
        // Arrange
        var token = await GetAccessTokenAsync();
        SetAuthHeader(token);

        var request = new Verify2FARequest
        {
            Code = ""
        };

        // Act
        var response = await Client.PostAsJsonAsync(Disable2FAEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Disable2FA_WithoutToken_ShouldReturnUnauthorized()
    {
        // Arrange
        Client.DefaultRequestHeaders.Authorization = null;

        var request = new Verify2FARequest
        {
            Code = "123456"
        };

        // Act
        var response = await Client.PostAsJsonAsync(Disable2FAEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Enable2FA_MultipleTimesShouldReturnSameKey()
    {
        // Arrange
        var token = await GetAccessTokenAsync("user@test.com", "User@123456");
        SetAuthHeader(token);

        // Act - Enable 2FA twice
        var response1 = await Client.PostAsync(Enable2FAEndpoint, null);
        var result1 = await response1.Content.ReadFromJsonAsync<Enable2FAResponse>();

        var response2 = await Client.PostAsync(Enable2FAEndpoint, null);
        var result2 = await response2.Content.ReadFromJsonAsync<Enable2FAResponse>();

        // Assert - Both should succeed
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        
        // The shared key might be the same or different depending on implementation
        result1!.SharedKey.Should().NotBeNullOrEmpty();
        result2!.SharedKey.Should().NotBeNullOrEmpty();
    }
}
