using System.Net;
using System.Net.Http.Json;
using EShop.Identity.IntegrationTests.Models;
using FluentAssertions;

namespace EShop.Identity.IntegrationTests.Auth;

/// <summary>
/// Integration tests for Password Reset flow
/// </summary>
[TestFixture]
public class PasswordResetTests : IntegrationTestBase
{
    private const string ForgotPasswordEndpoint = "/api/v1/auth/forgot-password";
    private const string ResetPasswordEndpoint = "/api/v1/auth/reset-password";
    private const string LoginEndpoint = "/api/v1/auth/login";

    [Test]
    public async Task ForgotPassword_WithValidEmail_ShouldReturnSuccess()
    {
        // Arrange
        var request = new ForgotPasswordRequest
        {
            Email = "admin@test.com"
        };

        // Act
        var response = await Client.PostAsJsonAsync(ForgotPasswordEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<ForgotPasswordResponse>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Message.Should().Contain("email exists");
    }

    [Test]
    public async Task ForgotPassword_WithNonExistentEmail_ShouldStillReturnSuccess()
    {
        // Arrange - Should not reveal if email exists (security)
        var request = new ForgotPasswordRequest
        {
            Email = "nonexistent@test.com"
        };

        // Act
        var response = await Client.PostAsJsonAsync(ForgotPasswordEndpoint, request);

        // Assert - Should succeed to prevent email enumeration
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<ForgotPasswordResponse>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
    }

    [Test]
    public async Task ForgotPassword_WithInactiveUser_ShouldStillReturnSuccess()
    {
        // Arrange - Should not reveal if user is active
        var request = new ForgotPasswordRequest
        {
            Email = "inactive@test.com"
        };

        // Act
        var response = await Client.PostAsJsonAsync(ForgotPasswordEndpoint, request);

        // Assert - Should succeed to prevent user enumeration
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task ForgotPassword_WithEmptyEmail_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new ForgotPasswordRequest
        {
            Email = ""
        };

        // Act
        var response = await Client.PostAsJsonAsync(ForgotPasswordEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ForgotPassword_WithInvalidEmailFormat_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new ForgotPasswordRequest
        {
            Email = "not-an-email"
        };

        // Act
        var response = await Client.PostAsJsonAsync(ForgotPasswordEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ResetPassword_WithInvalidToken_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new ResetPasswordRequest
        {
            UserId = "some-user-id",
            Token = "invalid-token",
            NewPassword = "NewPassword@123"
        };

        // Act
        var response = await Client.PostAsJsonAsync(ResetPasswordEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ResetPassword_WithNonExistentUser_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new ResetPasswordRequest
        {
            UserId = "non-existent-user-id",
            Token = "some-token",
            NewPassword = "NewPassword@123"
        };

        // Act
        var response = await Client.PostAsJsonAsync(ResetPasswordEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ResetPassword_WithEmptyUserId_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new ResetPasswordRequest
        {
            UserId = "",
            Token = "some-token",
            NewPassword = "NewPassword@123"
        };

        // Act
        var response = await Client.PostAsJsonAsync(ResetPasswordEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ResetPassword_WithEmptyToken_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new ResetPasswordRequest
        {
            UserId = "some-user-id",
            Token = "",
            NewPassword = "NewPassword@123"
        };

        // Act
        var response = await Client.PostAsJsonAsync(ResetPasswordEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task ResetPassword_WithWeakPassword_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new ResetPasswordRequest
        {
            UserId = "some-user-id",
            Token = "some-token",
            NewPassword = "weak"
        };

        // Act
        var response = await Client.PostAsJsonAsync(ResetPasswordEndpoint, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
