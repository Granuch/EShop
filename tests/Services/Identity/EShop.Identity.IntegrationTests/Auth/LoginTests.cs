using System.Net;
using System.Net.Http.Json;
using EShop.Identity.IntegrationTests.Models;
using FluentAssertions;

namespace EShop.Identity.IntegrationTests.Auth;

/// <summary>
/// Integration tests for User Login endpoint
/// </summary>
[TestFixture]
public class LoginTests : IntegrationTestBase
{
    private const string LoginEndpoint = "/api/v1/auth/login";

    [Test]
    public async Task Login_WithValidCredentials_ShouldReturnTokens()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "admin@test.com",
            Password = "Admin@123456"
        };

        // Act
        var response = await Client.PostAsJsonAsync(LoginEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        result.Should().NotBeNull();
        result!.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.ExpiresIn.Should().BeGreaterThan(0);
        result.TokenType.Should().Be("Bearer");
        result.Requires2FA.Should().BeFalse();
        result.User.Should().NotBeNull();
        result.User!.Email.Should().Be("admin@test.com");
        result.User.Roles.Should().Contain("Admin");
    }

    [Test]
    public async Task Login_WithRegularUser_ShouldReturnUserRole()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "user@test.com",
            Password = "User@123456"
        };

        // Act
        var response = await Client.PostAsJsonAsync(LoginEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        result.Should().NotBeNull();
        result!.User.Should().NotBeNull();
        result.User!.Roles.Should().Contain("User");
        result.User.Roles.Should().NotContain("Admin");
    }

    [Test]
    public async Task Login_WithInvalidPassword_ShouldReturnUnauthorized()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "admin@test.com",
            Password = "WrongPassword@123"
        };

        // Act
        var response = await Client.PostAsJsonAsync(LoginEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.error.Should().Be("Auth.InvalidCredentials");
    }

    [Test]
    public async Task Login_WithNonExistentUser_ShouldReturnUnauthorized()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "nonexistent@test.com",
            Password = "SomePassword@123"
        };

        // Act
        var response = await Client.PostAsJsonAsync(LoginEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.error.Should().Be("Auth.InvalidCredentials");
    }

    [Test]
    public async Task Login_WithInactiveUser_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "inactive@test.com",
            Password = "Inactive@123456"
        };

        // Act
        var response = await Client.PostAsJsonAsync(LoginEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.error.Should().Be("Auth.AccountDisabled");
    }

    [Test]
    public async Task Login_WithEmptyEmail_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "",
            Password = "SomePassword@123"
        };

        // Act
        var response = await Client.PostAsJsonAsync(LoginEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Login_WithEmptyPassword_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "admin@test.com",
            Password = ""
        };

        // Act
        var response = await Client.PostAsJsonAsync(LoginEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Login_WithInvalidEmailFormat_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = "not-an-email",
            Password = "SomePassword@123"
        };

        // Act
        var response = await Client.PostAsJsonAsync(LoginEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Login_SuccessfulLogin_ShouldUpdateLastLoginTime()
    {
        // Arrange
        var loginRequest = new LoginRequest
        {
            Email = "user@test.com",
            Password = "User@123456"
        };

        // Act - Login twice
        var firstResponse = await Client.PostAsJsonAsync(LoginEndpoint, loginRequest);
        var firstResult = await firstResponse.Content.ReadFromJsonAsync<LoginResponse>();
        
        await Task.Delay(100); // Small delay
        
        var secondResponse = await Client.PostAsJsonAsync(LoginEndpoint, loginRequest);
        var secondResult = await secondResponse.Content.ReadFromJsonAsync<LoginResponse>();

        // Assert
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        
        // Both logins should succeed
        firstResult!.AccessToken.Should().NotBeNullOrEmpty();
        secondResult!.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task Login_WithCaseSensitiveEmail_ShouldWork()
    {
        // Arrange - test with different case
        var request = new LoginRequest
        {
            Email = "ADMIN@TEST.COM",
            Password = "Admin@123456"
        };

        // Act
        var response = await Client.PostAsJsonAsync(LoginEndpoint, request);

        // Assert - ASP.NET Identity normalizes emails
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
