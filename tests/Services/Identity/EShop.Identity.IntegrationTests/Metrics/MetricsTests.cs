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
    public async Task MetricsEndpoint_ShouldDocumentIdentityLoginMetric()
    {
        // The old assertion checked for a bare "# HELP"/"# TYPE" anywhere in the payload, which
        // is prometheus-net's own exposition framing — true for any metric prometheus-net emits,
        // including ones this service never registered, so it could not catch our own metric
        // registration breaking. Pin the HELP/TYPE lines to the metric this service actually owns.
        var metrics = await MetricsHelper.GetPrometheusMetricsAsync(Client);

        metrics.Should().NotBeNullOrEmpty();
        metrics.Should().MatchRegex(@"# HELP identity_login_attempts_total\b");
        metrics.Should().MatchRegex(@"# TYPE identity_login_attempts_total\b");
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
    public async Task Metrics_ShouldContainDotNetRuntimeMetrics()
    {
        // The old assertion checked for the substring "dotnet" anywhere in the payload, which
        // matches on prometheus-net's own DotNetStats naming regardless of what this service
        // wired up itself, and would also match on unrelated text. Assert a concrete, always
        // -present runtime series instead, so a dropped runtime-metrics registration actually
        // fails this rather than passing by coincidence.
        var metrics = await MetricsHelper.GetPrometheusMetricsAsync(Client);

        MetricsHelper.MetricExists(metrics, "dotnet_collection_count_total").Should().BeTrue(
            "prometheus-net's DotNetStats should publish GC collection counts");
    }
}
