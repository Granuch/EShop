using System.Net;
using System.Net.Http.Json;
using EShop.Identity.IntegrationTests.Helpers;
using EShop.Identity.IntegrationTests.Models;
using FluentAssertions;

namespace EShop.Identity.IntegrationTests.Account;

/// <summary>
/// Integration tests for Password Change endpoint
/// </summary>
[TestFixture]
[Category("Integration")]
public class ChangePasswordTests : IntegrationTestBase
{
    private const string ChangePasswordEndpoint = "/api/v1/account/change-password";
    private const string RefreshEndpoint = "/api/v1/auth/refresh-token";

    private string _testUserId = null!;
    private string _testUserEmail = null!;
    private const string _testUserPassword = "Test@123456";

    [SetUp]
    public override async Task SetUpAsync()
    {
        await base.SetUpAsync();

        // Create isolated test user for each test
        _testUserEmail = $"test_{Guid.NewGuid()}@test.com";
        _testUserId = await CreateTestUserAsync(_testUserEmail, _testUserPassword);
    }

    [TearDown]
    public override async Task TearDownAsync()
    {
        // Clean up test user
        await DeleteTestUserAsync(_testUserId);
        await base.TearDownAsync();
    }

    [Test]
    public async Task ChangePassword_WithValidData_ShouldSucceed()
    {
        // Arrange
        var newPassword = "NewTest@123456";

        var accessToken = await Client.GetAccessTokenAsync(_testUserEmail, _testUserPassword);
        Client.SetBearerToken(accessToken);

        var request = new ChangePasswordRequest
        {
            CurrentPassword = _testUserPassword,
            NewPassword = newPassword
        };

        // Act
        var response = await Client.PostAsJsonAsync(ChangePasswordEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify can login with new password
        Client.ClearBearerToken();
        var loginResponse = await Client.LoginAsync(_testUserEmail, newPassword);
        loginResponse.Should().NotBeNull();
        loginResponse.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task ChangePassword_ShouldInvalidateRefreshTokens()
    {
        // Arrange
        var newPassword = "NewTest@123456";

        var (accessToken, refreshToken) = await Client.GetTokensAsync(_testUserEmail, _testUserPassword);
        Client.SetBearerToken(accessToken);

        var changeRequest = new ChangePasswordRequest
        {
            CurrentPassword = _testUserPassword,
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
    }

    [Test]
    public async Task ChangePassword_WithWrongCurrentPassword_ShouldReturnBadRequest()
    {
        // Arrange
        var accessToken = await Client.GetAccessTokenAsync(_testUserEmail, _testUserPassword);
        Client.SetBearerToken(accessToken);

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
        var accessToken = await Client.GetAccessTokenAsync(_testUserEmail, _testUserPassword);
        Client.SetBearerToken(accessToken);

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
        var accessToken = await Client.GetAccessTokenAsync(_testUserEmail, _testUserPassword);
        Client.SetBearerToken(accessToken);

        var request = new ChangePasswordRequest
        {
            CurrentPassword = _testUserPassword,
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
        Client.ClearBearerToken();

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
        var accessToken = await Client.GetAccessTokenAsync(_testUserEmail, _testUserPassword);
        Client.SetBearerToken(accessToken);

        var request = new ChangePasswordRequest
        {
            CurrentPassword = _testUserPassword,
            NewPassword = "weak" // Too weak
        };

        // Act
        var response = await Client.PostAsJsonAsync(ChangePasswordEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
