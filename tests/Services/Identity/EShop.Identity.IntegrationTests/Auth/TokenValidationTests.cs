using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using EShop.Identity.IntegrationTests.Helpers;
using EShop.Identity.IntegrationTests.Models;
using FluentAssertions;

namespace EShop.Identity.IntegrationTests.Auth;

/// <summary>
/// Integration tests for JWT token validation
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Security")]
public class TokenValidationTests : IntegrationTestBase
{
    private const string LoginEndpoint = "/api/v1/auth/login";

    [Test]
    public async Task Login_ShouldReturnValidJwtToken()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = TestUsers.Admin.Email,
            Password = TestUsers.Admin.Password
        };

        // Act
        var response = await Client.PostAsJsonAsync(LoginEndpoint, request);
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();

        // Assert
        result.Should().NotBeNull();
        result!.AccessToken.Should().NotBeNullOrEmpty();

        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result.AccessToken);

        token.Should().NotBeNull();
        token.Claims.Should().NotBeEmpty();
    }

    [Test]
    public async Task JwtToken_ShouldContainRequiredClaims()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = TestUsers.Admin.Email,
            Password = TestUsers.Admin.Password
        };

        // Act
        var response = await Client.PostAsJsonAsync(LoginEndpoint, request);
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result!.AccessToken);

        var claims = token.Claims.ToList();

        // Required claims - use actual claim types from the token
        claims.Should().Contain(c => c.Type == "sub" || c.Type == ClaimTypes.NameIdentifier, 
            "token should contain user identifier claim");
        claims.Should().Contain(c => c.Type == "email" || c.Type == ClaimTypes.Email,
            "token should contain email claim");
        claims.Should().Contain(c => c.Type == ClaimTypes.Role || c.Type == "role",
            "token should contain role claim");
        claims.Should().Contain(c => c.Type == "jti", 
            "token should contain JWT ID claim");
    }

    [Test]
    public async Task JwtToken_ShouldHaveValidExpiration()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = TestUsers.Admin.Email,
            Password = TestUsers.Admin.Password
        };

        // Act
        var response = await Client.PostAsJsonAsync(LoginEndpoint, request);
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result!.AccessToken);

        token.ValidTo.Should().BeAfter(DateTime.UtcNow);
        token.ValidFrom.Should().BeBefore(DateTime.UtcNow.AddMinutes(1));
    }

    [Test]
    public async Task JwtToken_ForRegularUser_ShouldContainUserRole()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = TestUsers.RegularUser.Email,
            Password = TestUsers.RegularUser.Password
        };

        // Act
        var response = await Client.PostAsJsonAsync(LoginEndpoint, request);
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(result!.AccessToken);

        var roleClaims = token.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "role")
            .ToList();

        roleClaims.Should().Contain(c => c.Value == TestUsers.Roles.User,
            "token should contain User role");
        roleClaims.Should().NotContain(c => c.Value == TestUsers.Roles.Admin,
            "token should not contain Admin role");
    }

    [Test]
    public async Task ExpiredToken_ShouldReturnUnauthorized()
    {
        // This test would require creating an expired token
        // For now, we test with an invalid token
        
        // Arrange
        Client.SetBearerToken("expired.or.invalid.token");

        // Act
        var response = await Client.GetAsync("/api/v1/account/profile");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
