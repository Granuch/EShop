using System.Net;
using System.Net.Http.Json;
using EShop.Identity.IntegrationTests.Helpers;
using EShop.Identity.IntegrationTests.Models;
using FluentAssertions;

namespace EShop.Identity.IntegrationTests.Metrics;

/// <summary>
/// Integration tests for Prometheus metrics
/// </summary>
[TestFixture]
[Category("Integration")]
[Category("Metrics")]
public class MetricsTests : IntegrationTestBase
{
    private const string LoginEndpoint = "/api/v1/auth/login";

    [Test]
    public async Task Login_Success_ShouldIncrementMetrics()
    {
        // Arrange
        var metricsBefore = await MetricsHelper.GetPrometheusMetricsAsync(Client);
        var totalBefore = MetricsHelper.GetMetricTotal(metricsBefore, "identity_login_attempts_total");

        var request = new LoginRequest
        {
            Email = TestUsers.Admin.Email,
            Password = TestUsers.Admin.Password
        };

        // Act
        var response = await Client.PostAsJsonAsync(LoginEndpoint, request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert
        var metricsAfter = await MetricsHelper.GetPrometheusMetricsAsync(Client);
        var totalAfter = MetricsHelper.GetMetricTotal(metricsAfter, "identity_login_attempts_total");

        // Verify that total count increased
        totalAfter.Should().BeGreaterThan(totalBefore, 
            "successful login should increment the login attempts counter");
    }

    [Test]
    public async Task Login_Failed_ShouldIncrementFailureMetrics()
    {
        // Arrange
        var metricsBefore = await MetricsHelper.GetPrometheusMetricsAsync(Client);
        var totalBefore = MetricsHelper.GetMetricTotal(metricsBefore, "identity_login_attempts_total");

        var request = new LoginRequest
        {
            Email = TestUsers.Admin.Email,
            Password = "WrongPassword@123"
        };

        // Act
        var response = await Client.PostAsJsonAsync(LoginEndpoint, request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Assert
        var metricsAfter = await MetricsHelper.GetPrometheusMetricsAsync(Client);
        var totalAfter = MetricsHelper.GetMetricTotal(metricsAfter, "identity_login_attempts_total");

        // Verify that total count increased
        totalAfter.Should().BeGreaterThan(totalBefore,
            "failed login should increment the login attempts counter");
    }

    [Test]
    public async Task MetricsEndpoint_ShouldReturnPrometheusFormat()
    {
        // Act
        var metrics = await MetricsHelper.GetPrometheusMetricsAsync(Client);

        // Assert
        metrics.Should().NotBeNullOrEmpty();
        metrics.Should().Contain("# HELP");
        metrics.Should().Contain("# TYPE");
    }

    [Test]
    public async Task Metrics_ShouldContainIdentitySpecificMetrics()
    {
        // Arrange - Perform a login so labeled counters are published
        var request = new LoginRequest
        {
            Email = TestUsers.Admin.Email,
            Password = TestUsers.Admin.Password
        };
        await Client.PostAsJsonAsync(LoginEndpoint, request);

        // Act
        var metrics = await MetricsHelper.GetPrometheusMetricsAsync(Client);

        // Assert - Check for expected metric names
        MetricsHelper.MetricExists(metrics, "identity_login_attempts_total").Should().BeTrue(
            "login attempts metric should be present after a login attempt");
    }

    [Test]
    public async Task Metrics_ShouldContainStandardDotNetMetrics()
    {
        // Act
        var metrics = await MetricsHelper.GetPrometheusMetricsAsync(Client);

        // Assert - Check for standard .NET metrics
        metrics.Should().Contain("dotnet", "should contain standard .NET runtime metrics");
    }
}
