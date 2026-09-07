using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace EShop.Identity.IntegrationTests.HealthChecks;

/// <summary>
/// Integration tests for Health Check endpoints
/// </summary>
[TestFixture]
public class HealthCheckTests : IntegrationTestBase
{
    [Test]
    public async Task HealthCheck_ShouldReturnHealthy()
    {
        // Act
        var response = await Client.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Healthy");
    }

    [Test]
    public async Task LivenessCheck_ShouldReturnHealthy()
    {
        // Act
        var response = await Client.GetAsync("/health/live");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Healthy");
    }

    [Test]
    public async Task ReadinessCheck_ShouldReturnHealthy()
    {
        // Act
        var response = await Client.GetAsync("/health/ready");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Healthy");
    }

    /// <summary>
    /// The writer itself is unit-tested (<c>HealthResponseWriterTests</c>); this proves the
    /// endpoints are actually wired to it rather than to
    /// <c>UIResponseWriter.WriteHealthCheckUIResponse</c>, which serialises descriptions, data
    /// and exception messages onto an anonymous endpoint (SEC-07).
    /// </summary>
    [TestCase("/health")]
    [TestCase("/health/ready")]
    [TestCase("/health/live")]
    public async Task HealthEndpoints_ShouldReturnNamesAndStatusesOnly(string path)
    {
        var response = await Client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        root.TryGetProperty("status", out _).Should().BeTrue();

        var checks = root.GetProperty("checks");
        foreach (var check in checks.EnumerateArray())
        {
            check.TryGetProperty("name", out _).Should().BeTrue();
            check.TryGetProperty("status", out _).Should().BeTrue();

            check.TryGetProperty("description", out _).Should().BeFalse(
                "a description can carry infrastructure detail and the endpoint is anonymous");
            check.TryGetProperty("data", out _).Should().BeFalse();
            check.TryGetProperty("exception", out _).Should().BeFalse();
        }
    }

    [Test]
    public async Task MetricsEndpoint_ShouldReturnMetrics()
    {
        // Act
        var response = await Client.GetAsync("/metrics");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var content = await response.Content.ReadAsStringAsync();
        // Prometheus metrics format
        content.Should().Contain("# HELP");
    }
}
