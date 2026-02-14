using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using EShop.Identity.IntegrationTests.Models;
using FluentAssertions;

namespace EShop.Identity.IntegrationTests.Scenarios;

/// <summary>
/// End-to-end integration tests covering complete user flows
/// Uses seeded test users to avoid email confirmation issues
/// </summary>
[TestFixture]
public class UserFlowTests : IntegrationTestBase
{
    private const string LoginEndpoint = "/api/v1/auth/login";
    private const string RefreshEndpoint = "/api/v1/auth/refresh-token";
    private const string RevokeEndpoint = "/api/v1/auth/revoke-token";
    private const string ProfileEndpoint = "/api/v1/account/profile";
    private const string ChangePasswordEndpoint = "/api/v1/account/change-password";

    private void SetAuthHeader(string token)
    {
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [Test]
    public async Task CompleteLoginAndProfileFlow()
    {
        // Step 1: Login with seeded user
        var email = "user@test.com";
        var password = "User@123456";

        var loginRequest = new LoginRequest
        {
            Email = email,
            Password = password
        };

        var loginResponse = await Client.PostAsJsonAsync(LoginEndpoint, loginRequest);
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        loginResult!.AccessToken.Should().NotBeNullOrEmpty();
        loginResult.RefreshToken.Should().NotBeNullOrEmpty();
        loginResult.User!.Email.Should().Be(email);

        // Step 2: Access protected resource (profile)
        SetAuthHeader(loginResult.AccessToken);
        var profileResponse = await Client.GetAsync(ProfileEndpoint);
        profileResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var profile = await profileResponse.Content.ReadFromJsonAsync<UserProfileResponse>();
        profile!.Email.Should().Be(email);

        // Step 3: Refresh token
        var refreshRequest = new RefreshTokenRequest
        {
            RefreshToken = loginResult.RefreshToken
        };

        var refreshResponse = await Client.PostAsJsonAsync(RefreshEndpoint, refreshRequest);
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshResult = await refreshResponse.Content.ReadFromJsonAsync<RefreshTokenResponse>();
        refreshResult!.AccessToken.Should().NotBeNullOrEmpty();
        refreshResult.RefreshToken.Should().NotBe(loginResult.RefreshToken); // Token rotation

        // Step 4: Use new token
        SetAuthHeader(refreshResult.AccessToken);
        var newProfileResponse = await Client.GetAsync(ProfileEndpoint);
        newProfileResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 5: Logout (revoke token)
        var revokeRequest = new RevokeTokenRequest
        {
            RefreshToken = refreshResult.RefreshToken
        };

        var revokeResponse = await Client.PostAsJsonAsync(RevokeEndpoint, revokeRequest);
        revokeResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Step 6: Verify token is invalid
        var invalidRefreshRequest = new RefreshTokenRequest
        {
            RefreshToken = refreshResult.RefreshToken
        };

        var invalidRefreshResponse = await Client.PostAsJsonAsync(RefreshEndpoint, invalidRefreshRequest);
        invalidRefreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task PasswordChangeFlowWithTokenInvalidation()
    {
        // Step 1: Login
        var email = "admin@test.com";
        var originalPassword = "Admin@123456";
        var newPassword = "Changed@Admin123";

        var loginRequest = new LoginRequest { Email = email, Password = originalPassword };
        var loginResponse = await Client.PostAsJsonAsync(LoginEndpoint, loginRequest);
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        // Step 2: Change password
        SetAuthHeader(loginResult!.AccessToken);
        var changeRequest = new ChangePasswordRequest
        {
            CurrentPassword = originalPassword,
            NewPassword = newPassword
        };

        var changeResponse = await Client.PostAsJsonAsync(ChangePasswordEndpoint, changeRequest);
        changeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 3: Verify old refresh token is invalid
        var refreshRequest = new RefreshTokenRequest { RefreshToken = loginResult.RefreshToken };
        var refreshResponse = await Client.PostAsJsonAsync(RefreshEndpoint, refreshRequest);
        refreshResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Step 4: Verify old password doesn't work
        Client.DefaultRequestHeaders.Authorization = null;
        var oldLoginRequest = new LoginRequest { Email = email, Password = originalPassword };
        var oldLoginResponse = await Client.PostAsJsonAsync(LoginEndpoint, oldLoginRequest);
        oldLoginResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Step 5: Verify new password works
        var newLoginRequest = new LoginRequest { Email = email, Password = newPassword };
        var newLoginResponse = await Client.PostAsJsonAsync(LoginEndpoint, newLoginRequest);
        newLoginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 6: Restore original password for other tests
        var newLoginResult = await newLoginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        SetAuthHeader(newLoginResult!.AccessToken);
        var restoreRequest = new ChangePasswordRequest
        {
            CurrentPassword = newPassword,
            NewPassword = originalPassword
        };
        await Client.PostAsJsonAsync(ChangePasswordEndpoint, restoreRequest);
    }

    [Test]
    public async Task MultipleSessionsFlow()
    {
        // Step 1: Login multiple times (simulate multiple devices)
        var loginRequest = new LoginRequest
        {
            Email = "user@test.com",
            Password = "User@123456"
        };

        var session1 = await Client.PostAsJsonAsync(LoginEndpoint, loginRequest);
        var result1 = await session1.Content.ReadFromJsonAsync<LoginResponse>();

        var session2 = await Client.PostAsJsonAsync(LoginEndpoint, loginRequest);
        var result2 = await session2.Content.ReadFromJsonAsync<LoginResponse>();

        var session3 = await Client.PostAsJsonAsync(LoginEndpoint, loginRequest);
        var result3 = await session3.Content.ReadFromJsonAsync<LoginResponse>();

        // Step 2: All sessions should have valid tokens
        result1!.AccessToken.Should().NotBeNullOrEmpty();
        result2!.AccessToken.Should().NotBeNullOrEmpty();
        result3!.AccessToken.Should().NotBeNullOrEmpty();

        // Step 3: All refresh tokens should be different
        result1.RefreshToken.Should().NotBe(result2.RefreshToken);
        result2.RefreshToken.Should().NotBe(result3.RefreshToken);

        // Step 4: All tokens should work
        SetAuthHeader(result1.AccessToken);
        var profile1 = await Client.GetAsync(ProfileEndpoint);
        profile1.StatusCode.Should().Be(HttpStatusCode.OK);

        SetAuthHeader(result2.AccessToken);
        var profile2 = await Client.GetAsync(ProfileEndpoint);
        profile2.StatusCode.Should().Be(HttpStatusCode.OK);

        SetAuthHeader(result3.AccessToken);
        var profile3 = await Client.GetAsync(ProfileEndpoint);
        profile3.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task ProfileUpdateFlow()
    {
        // Step 1: Login
        var loginRequest = new LoginRequest { Email = "user@test.com", Password = "User@123456" };
        var loginResponse = await Client.PostAsJsonAsync(LoginEndpoint, loginRequest);
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        SetAuthHeader(loginResult!.AccessToken);

        // Step 2: Get current profile
        var getResponse = await Client.GetAsync(ProfileEndpoint);
        var originalProfile = await getResponse.Content.ReadFromJsonAsync<UserProfileResponse>();

        // Step 3: Update profile
        var updateRequest = new UpdateProfileRequest
        {
            FirstName = "Updated",
            LastName = "Profile",
            ProfilePictureUrl = "https://example.com/new-picture.jpg"
        };

        var updateResponse = await Client.PutAsJsonAsync(ProfileEndpoint, updateRequest);
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Step 4: Verify update
        var verifyResponse = await Client.GetAsync(ProfileEndpoint);
        var updatedProfile = await verifyResponse.Content.ReadFromJsonAsync<UserProfileResponse>();

        updatedProfile!.FirstName.Should().Be("Updated");
        updatedProfile.LastName.Should().Be("Profile");
        updatedProfile.ProfilePictureUrl.Should().Be("https://example.com/new-picture.jpg");

        // Step 5: Restore original values
        var restoreRequest = new UpdateProfileRequest
        {
            FirstName = originalProfile!.FirstName,
            LastName = originalProfile.LastName,
            ProfilePictureUrl = originalProfile.ProfilePictureUrl
        };
        await Client.PutAsJsonAsync(ProfileEndpoint, restoreRequest);
    }
}
