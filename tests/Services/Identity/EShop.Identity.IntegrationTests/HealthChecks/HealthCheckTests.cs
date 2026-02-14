using System.Net;
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
