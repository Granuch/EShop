using System.Net;
using System.Net.Http.Json;
using EShop.Identity.IntegrationTests.Helpers;
using EShop.Identity.IntegrationTests.Models;
using FluentAssertions;

namespace EShop.Identity.IntegrationTests.Account;

/// <summary>
/// Integration tests for User Profile endpoints
/// </summary>
[TestFixture]
[Category("Integration")]
public class ProfileTests : AuthenticatedIntegrationTestBase
{
    private const string ProfileEndpoint = "/api/v1/account/profile";

    [Test]
    public async Task GetProfile_WithValidToken_ShouldReturnProfile()
    {
        // Act
        var response = await Client.GetAsync(ProfileEndpoint);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var profile = await response.Content.ReadFromJsonAsync<UserProfileResponse>();
        profile.Should().NotBeNull();
        profile!.Email.Should().Be(TestUsers.Admin.Email);
        profile.FirstName.Should().Be(TestUsers.Admin.FirstName);
        profile.LastName.Should().Be(TestUsers.Admin.LastName);
        profile.EmailConfirmed.Should().BeTrue();
        profile.IsActive.Should().BeTrue();
        profile.Roles.Should().Contain(TestUsers.Roles.Admin);
    }

    [Test]
    public async Task GetProfile_WithRegularUserToken_ShouldReturnUserProfile()
    {
        // Arrange - Login as regular user
        Client.ClearBearerToken();
        var token = await Client.GetAccessTokenAsync(TestUsers.RegularUser.Email, TestUsers.RegularUser.Password);
        Client.SetBearerToken(token);

        // Act
        var response = await Client.GetAsync(ProfileEndpoint);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var profile = await response.Content.ReadFromJsonAsync<UserProfileResponse>();
        profile.Should().NotBeNull();
        profile!.Email.Should().Be(TestUsers.RegularUser.Email);
        profile.Roles.Should().Contain(TestUsers.Roles.User);
        profile.Roles.Should().NotContain(TestUsers.Roles.Admin);
    }

    [Test]
    public async Task GetProfile_WithoutToken_ShouldReturnUnauthorized()
    {
        // Arrange - No auth header
        Client.ClearBearerToken();

        // Act
        var response = await Client.GetAsync(ProfileEndpoint);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task GetProfile_WithInvalidToken_ShouldReturnUnauthorized()
    {
        // Arrange
        Client.SetBearerToken("invalid-jwt-token");

        // Act
        var response = await Client.GetAsync(ProfileEndpoint);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task GetProfile_WithExpiredToken_ShouldReturnUnauthorized()
    {
        // Arrange - Create an expired-looking token (this won't actually be valid)
        Client.SetBearerToken("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwiZXhwIjoxfQ.invalid");

        // Act
        var response = await Client.GetAsync(ProfileEndpoint);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task UpdateProfile_WithValidData_ShouldSucceed()
    {
        // Test uses Admin user from base class which is already authenticated

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
        Client.ClearBearerToken();

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
