using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using EShop.Identity.IntegrationTests.Helpers;
using EShop.Identity.IntegrationTests.Models;
using FluentAssertions;

namespace EShop.Identity.IntegrationTests.Performance;

/// <summary>
/// Integration tests for performance benchmarks
/// Note: These tests have relaxed thresholds for CI/CD environments
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Performance")]
public class LoginPerformanceTests : IntegrationTestBase
{
    private const string LoginEndpoint = "/api/v1/auth/login";

    [Test]
    public async Task Login_ShouldCompleteWithinAcceptableTime()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = TestUsers.Admin.Email,
            Password = TestUsers.Admin.Password
        };

        var stopwatch = Stopwatch.StartNew();

        // Act
        var response = await Client.PostAsJsonAsync(LoginEndpoint, request);

        stopwatch.Stop();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(3000, 
            "login should complete within 3 seconds (relaxed for CI/CD)");
    }

    [Test]
    public async Task ParallelLogins_ShouldMaintainAcceptableLatency()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = TestUsers.Admin.Email,
            Password = TestUsers.Admin.Password
        };

        // Act - Simulate 20 parallel login requests
        var stopwatch = Stopwatch.StartNew();

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => Client.PostAsJsonAsync(LoginEndpoint, request));

        var responses = await Task.WhenAll(tasks);

        stopwatch.Stop();

        // Assert
        responses.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.OK));

        var averageTime = stopwatch.ElapsedMilliseconds / 20.0;
        averageTime.Should().BeLessThan(2000, 
            "average login time should be less than 2s under load (relaxed for CI/CD)");
    }

    [Test]
    public async Task SequentialLogins_ShouldMaintainConsistentPerformance()
    {
        // Arrange
        var request = new LoginRequest
        {
            Email = TestUsers.Admin.Email,
            Password = TestUsers.Admin.Password
        };

        var executionTimes = new List<long>();

        // Act - Perform 10 sequential logins
        for (int i = 0; i < 10; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            var response = await Client.PostAsJsonAsync(LoginEndpoint, request);
            stopwatch.Stop();

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            executionTimes.Add(stopwatch.ElapsedMilliseconds);
        }

        // Assert
        var averageTime = executionTimes.Average();
        var maxTime = executionTimes.Max();
        var minTime = executionTimes.Min();

        averageTime.Should().BeLessThan(3000, 
            "average execution time should be reasonable (relaxed for CI/CD)");
        maxTime.Should().BeLessThan(5000, 
            "max execution time should not spike too high (relaxed for CI/CD)");

        // Check for consistency (max should not be more than 5x min)
        if (minTime > 0)
        {
            (maxTime / (double)minTime).Should().BeLessThan(5, 
                "execution times should be relatively consistent");
        }
    }
}

/// <summary>
/// Integration tests for token refresh performance
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Performance")]
public class RefreshTokenPerformanceTests : IntegrationTestBase
{
    private const string RefreshEndpoint = "/api/v1/auth/refresh-token";

    [Test]
    public async Task RefreshToken_ShouldCompleteWithinAcceptableTime()
    {
        // Arrange
        var (_, refreshToken) = await Client.GetTokensAsync(TestUsers.Admin.Email, TestUsers.Admin.Password);

        var request = new RefreshTokenRequest
        {
            RefreshToken = refreshToken
        };

        var stopwatch = Stopwatch.StartNew();

        // Act
        var response = await Client.PostAsJsonAsync(RefreshEndpoint, request);

        stopwatch.Stop();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(2000, 
            "token refresh should complete within 2s (relaxed for CI/CD)");
    }

    [Test]
    public async Task ParallelRefreshRequests_ShouldMaintainPerformance()
    {
        // Arrange - Get multiple refresh tokens
        var tokens = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => Client.GetTokensAsync(TestUsers.Admin.Email, TestUsers.Admin.Password)));

        var stopwatch = Stopwatch.StartNew();

        // Act - Refresh all tokens in parallel
        var tasks = tokens.Select(t => Client.PostAsJsonAsync(RefreshEndpoint, new RefreshTokenRequest
        {
            RefreshToken = t.RefreshToken
        }));

        var responses = await Task.WhenAll(tasks);

        stopwatch.Stop();

        // Assert
        var averageTime = stopwatch.ElapsedMilliseconds / 10.0;
        averageTime.Should().BeLessThan(2000, 
            "average refresh time should be acceptable (relaxed for CI/CD)");
    }
}

/// <summary>
/// Integration tests for API response time under various loads
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Performance")]
public class ApiLatencyTests : IntegrationTestBase
{
    [Test]
    public async Task HealthCheck_ShouldRespondQuickly()
    {
        // Arrange
        var stopwatch = Stopwatch.StartNew();

        // Act
        var response = await Client.GetAsync("/health");

        stopwatch.Stop();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000, 
            "health check should be fast (relaxed for CI/CD)");
    }

    [Test]
    public async Task GetProfile_ShouldRespondWithinAcceptableTime()
    {
        // Arrange
        var token = await Client.GetAccessTokenAsync(TestUsers.Admin.Email, TestUsers.Admin.Password);
        Client.SetBearerToken(token);

        var stopwatch = Stopwatch.StartNew();

        // Act
        var response = await Client.GetAsync("/api/v1/account/profile");

        stopwatch.Stop();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(2000, 
            "profile retrieval should be reasonably fast (relaxed for CI/CD)");
    }

    [Test]
    public async Task MixedWorkload_ShouldMaintainAcceptableLatency()
    {
        // Arrange
        var token = await Client.GetAccessTokenAsync(TestUsers.Admin.Email, TestUsers.Admin.Password);
        Client.SetBearerToken(token);

        var stopwatch = Stopwatch.StartNew();

        // Act - Mix of read and write operations
        var tasks = new[]
        {
            Client.GetAsync("/health"),
            Client.GetAsync("/api/v1/account/profile"),
            Client.GetAsync("/health"),
            Client.GetAsync("/api/v1/account/profile")
        };

        var responses = await Task.WhenAll(tasks);

        stopwatch.Stop();

        // Assert
        responses.Should().AllSatisfy(r => r.IsSuccessStatusCode.Should().BeTrue());

        var averageLatency = stopwatch.ElapsedMilliseconds / (double)tasks.Length;
        averageLatency.Should().BeLessThan(2000, 
            "mixed workload should maintain reasonable performance (relaxed for CI/CD)");
    }
}
