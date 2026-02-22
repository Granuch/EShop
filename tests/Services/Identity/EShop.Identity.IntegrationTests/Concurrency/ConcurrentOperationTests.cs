using System.Net;
using System.Net.Http.Json;
using EShop.Identity.IntegrationTests.Helpers;
using EShop.Identity.IntegrationTests.Models;
using FluentAssertions;

namespace EShop.Identity.IntegrationTests.Concurrency;

/// <summary>
/// Integration tests for concurrent operations
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Concurrency")]
public class ConcurrentLoginTests : IntegrationTestBase
{
    private const string LoginEndpoint = "/api/v1/auth/login";

    [Test]
    public async Task ConcurrentLogins_SameUser_ShouldAllSucceed()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = TestUsers.Admin.Email,
            Password = TestUsers.Admin.Password
        };

        // Act - Simulate 10 concurrent login requests
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Client.PostAsJsonAsync(LoginEndpoint, request));

        var responses = await Task.WhenAll(tasks);

        // Assert
        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.OK));

        var results = await Task.WhenAll(responses.Select(r => r.Content.ReadFromJsonAsync<LoginResponse>()));
        results.Should().AllSatisfy(result =>
        {
            result.Should().NotBeNull();
            result!.AccessToken.Should().NotBeNullOrEmpty();
            result.RefreshToken.Should().NotBeNullOrEmpty();
        });
    }

    [Test]
    public async Task ConcurrentLogins_DifferentUsers_ShouldAllSucceed()
    {
        // Arrange
        var users = new[]
        {
            (TestUsers.Admin.Email, TestUsers.Admin.Password),
            (TestUsers.RegularUser.Email, TestUsers.RegularUser.Password)
        };

        // Act - Simulate concurrent logins from different users
        var tasks = users
            .SelectMany(user => Enumerable.Range(0, 5)
                .Select(_ => Client.PostAsJsonAsync(LoginEndpoint, new LoginRequest
                {
                    Email = user.Email,
                    Password = user.Password
                })));

        var responses = await Task.WhenAll(tasks);

        // Assert
        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.OK));
    }

    [Test]
    public async Task ConcurrentPasswordChanges_ShouldHandleRaceConditions()
    {
        // Arrange - Create test user
        var testEmail = $"concurrent_{Guid.NewGuid()}@test.com";
        var testPassword = "Test@123456";
        var testUserId = await CreateTestUserAsync(testEmail, testPassword);

        try
        {
            var accessToken = await Client.GetAccessTokenAsync(testEmail, testPassword);
            Client.SetBearerToken(accessToken);

            // Act - Try to change password concurrently (race condition)
            var tasks = Enumerable.Range(0, 5)
                .Select(i => Client.PostAsJsonAsync("/api/v1/account/change-password", new ChangePasswordRequest
                {
                    CurrentPassword = testPassword,
                    NewPassword = $"NewPassword{i}@123456"
                }));

            var responses = await Task.WhenAll(tasks);

            // Assert - Only one should succeed, others should fail due to concurrency
                var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
                var failureCount = responses.Count(r => r.StatusCode != HttpStatusCode.OK);

                successCount.Should().BeGreaterThanOrEqualTo(1, "at least one password change should succeed");
                failureCount.Should().BeGreaterThanOrEqualTo(1, "some concurrent attempts should fail");
        }
        finally
        {
            await DeleteTestUserAsync(testUserId);
        }
    }

    [Test]
    public async Task ConcurrentRefreshTokenRequests_ShouldHandleCorrectly()
    {
        // Arrange
        var (accessToken, refreshToken) = await Client.GetTokensAsync(TestUsers.Admin.Email, TestUsers.Admin.Password);

        // Act - Try to use the same refresh token multiple times concurrently
        var tasks = Enumerable.Range(0, 5)
            .Select(_ => Client.PostAsJsonAsync("/api/v1/auth/refresh-token", new RefreshTokenRequest
            {
                RefreshToken = refreshToken
            }));

        var responses = await Task.WhenAll(tasks);

        // Assert - Depending on implementation, either all succeed with same token or only one succeeds
        // This tests the service's handling of concurrent refresh token usage
        var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        successCount.Should().BeGreaterThanOrEqualTo(1, "at least one refresh should succeed");
    }
}

/// <summary>
/// Integration tests for concurrent profile updates
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Concurrency")]
public class ConcurrentProfileUpdateTests : IntegrationTestBase
{
    private static readonly string[] FirstNames = ["Alice", "Bob", "Charlie", "Diana", "Eve"];
    private static readonly string[] LastNames = ["Smith", "Johnson", "Williams", "Brown", "Jones"];

    [Test]
    public async Task ConcurrentProfileUpdates_ShouldHandleRaceConditions()
    {
        // Arrange - Create test user
        var testEmail = $"concurrent_{Guid.NewGuid()}@test.com";
        var testPassword = "Test@123456";
        var testUserId = await CreateTestUserAsync(testEmail, testPassword);

        try
        {
            var accessToken = await Client.GetAccessTokenAsync(testEmail, testPassword);
            Client.SetBearerToken(accessToken);

            // Act - Try to update profile concurrently
            var tasks = Enumerable.Range(0, 5)
                .Select(i => Client.PutAsJsonAsync("/api/v1/account/profile", new UpdateProfileRequest
                {
                    FirstName = FirstNames[i],
                    LastName = LastNames[i]
                }));

            var responses = await Task.WhenAll(tasks);

            // Assert - At least some should succeed (race conditions may cause some to fail)
            var successCount = responses.Count(r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.NoContent);
            successCount.Should().BeGreaterThanOrEqualTo(1, 
                "at least one concurrent update should succeed");

            // Verify final state is consistent
            Client.SetBearerToken(accessToken);
            var profileResponse = await Client.GetAsync("/api/v1/account/profile");
            profileResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var profile = await profileResponse.Content.ReadFromJsonAsync<UserProfileResponse>();
            profile.Should().NotBeNull();

            // Verify that the profile exists and has valid data
            profile!.FirstName.Should().NotBeNullOrEmpty("profile should have a first name");
            profile.LastName.Should().NotBeNullOrEmpty("profile should have a last name");
        }
        finally
        {
            await DeleteTestUserAsync(testUserId);
        }
    }
}
