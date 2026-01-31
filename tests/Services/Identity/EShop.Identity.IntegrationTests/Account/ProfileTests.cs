using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EShop.Identity.IntegrationTests.Models;
using FluentAssertions;

namespace EShop.Identity.IntegrationTests.Account;

/// <summary>
/// Integration tests for User Profile endpoints
/// </summary>
[TestFixture]
public class ProfileTests : IntegrationTestBase
{
    private const string LoginEndpoint = "/api/v1/auth/login";
    private const string ProfileEndpoint = "/api/v1/account/profile";

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
    public async Task GetProfile_WithValidToken_ShouldReturnProfile()
    {
        // Arrange
        var token = await GetAccessTokenAsync();
        SetAuthHeader(token);

        // Act
        var response = await Client.GetAsync(ProfileEndpoint);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var profile = await response.Content.ReadFromJsonAsync<UserProfileResponse>();
        profile.Should().NotBeNull();
        profile!.Email.Should().Be("admin@test.com");
        profile.FirstName.Should().Be("Admin");
        profile.LastName.Should().Be("Test");
        profile.EmailConfirmed.Should().BeTrue();
        profile.IsActive.Should().BeTrue();
        profile.Roles.Should().Contain("Admin");
    }

    [Test]
    public async Task GetProfile_WithRegularUserToken_ShouldReturnUserProfile()
    {
        // Arrange
        var token = await GetAccessTokenAsync("user@test.com", "User@123456");
        SetAuthHeader(token);

        // Act
        var response = await Client.GetAsync(ProfileEndpoint);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var profile = await response.Content.ReadFromJsonAsync<UserProfileResponse>();
        profile.Should().NotBeNull();
        profile!.Email.Should().Be("user@test.com");
        profile.Roles.Should().Contain("User");
        profile.Roles.Should().NotContain("Admin");
    }

    [Test]
    public async Task GetProfile_WithoutToken_ShouldReturnUnauthorized()
    {
        // Arrange - No auth header
        Client.DefaultRequestHeaders.Authorization = null;

        // Act
        var response = await Client.GetAsync(ProfileEndpoint);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task GetProfile_WithInvalidToken_ShouldReturnUnauthorized()
    {
        // Arrange
        SetAuthHeader("invalid-jwt-token");

        // Act
        var response = await Client.GetAsync(ProfileEndpoint);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task GetProfile_WithExpiredToken_ShouldReturnUnauthorized()
    {
        // Arrange - Create an expired-looking token (this won't actually be valid)
        SetAuthHeader("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwiZXhwIjoxfQ.invalid");

        // Act
        var response = await Client.GetAsync(ProfileEndpoint);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UpdateProfile_WithValidData_ShouldSucceed()
    {
        // Arrange
        var token = await GetAccessTokenAsync();
        SetAuthHeader(token);
        
        var request = new UpdateProfileRequest
        {
            FirstName = "Updated",
            LastName = "Name"
        };

        // Act
        var response = await Client.PutAsJsonAsync(ProfileEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify the update
        var getResponse = await Client.GetAsync(ProfileEndpoint);
        var profile = await getResponse.Content.ReadFromJsonAsync<UserProfileResponse>();
        profile!.FirstName.Should().Be("Updated");
        profile.LastName.Should().Be("Name");
    }

    [Test]
    public async Task UpdateProfile_WithProfilePictureUrl_ShouldSucceed()
    {
        // Arrange
        var token = await GetAccessTokenAsync();
        SetAuthHeader(token);
        
        var request = new UpdateProfileRequest
        {
            FirstName = "Admin",
            LastName = "Test",
            ProfilePictureUrl = "https://example.com/picture.jpg"
        };

        // Act
        var response = await Client.PutAsJsonAsync(ProfileEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify the update
        var getResponse = await Client.GetAsync(ProfileEndpoint);
        var profile = await getResponse.Content.ReadFromJsonAsync<UserProfileResponse>();
        profile!.ProfilePictureUrl.Should().Be("https://example.com/picture.jpg");
    }

    [Test]
    public async Task UpdateProfile_WithEmptyFirstName_ShouldReturnBadRequest()
    {
        // Arrange
        var token = await GetAccessTokenAsync();
        SetAuthHeader(token);
        
        var request = new UpdateProfileRequest
        {
            FirstName = "",
            LastName = "Test"
        };

        // Act
        var response = await Client.PutAsJsonAsync(ProfileEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task UpdateProfile_WithEmptyLastName_ShouldReturnBadRequest()
    {
        // Arrange
        var token = await GetAccessTokenAsync();
        SetAuthHeader(token);
        
        var request = new UpdateProfileRequest
        {
            FirstName = "Admin",
            LastName = ""
        };

        // Act
        var response = await Client.PutAsJsonAsync(ProfileEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task UpdateProfile_WithInvalidUrl_ShouldReturnBadRequest()
    {
        // Arrange
        var token = await GetAccessTokenAsync();
        SetAuthHeader(token);
        
        var request = new UpdateProfileRequest
        {
            FirstName = "Admin",
            LastName = "Test",
            ProfilePictureUrl = "not-a-valid-url"
        };

        // Act
        var response = await Client.PutAsJsonAsync(ProfileEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task UpdateProfile_WithoutToken_ShouldReturnUnauthorized()
    {
        // Arrange
        Client.DefaultRequestHeaders.Authorization = null;
        
        var request = new UpdateProfileRequest
        {
            FirstName = "Test",
            LastName = "User"
        };

        // Act
        var response = await Client.PutAsJsonAsync(ProfileEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UpdateProfile_WithTooLongFirstName_ShouldReturnBadRequest()
    {
        // Arrange
        var token = await GetAccessTokenAsync();
        SetAuthHeader(token);
        
        var request = new UpdateProfileRequest
        {
            FirstName = new string('A', 51), // > 50 chars
            LastName = "Test"
        };

        // Act
        var response = await Client.PutAsJsonAsync(ProfileEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task UpdateProfile_WithSpecialCharsInName_ShouldSucceed()
    {
        // Arrange
        var token = await GetAccessTokenAsync();
        SetAuthHeader(token);
        
        var request = new UpdateProfileRequest
        {
            FirstName = "Mary-Jane",
            LastName = "O'Connor"
        };

        // Act
        var response = await Client.PutAsJsonAsync(ProfileEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
