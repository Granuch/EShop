using System.Net;
using FluentAssertions;

namespace EShop.Ordering.IntegrationTests.HealthChecks;

/// <summary>
/// Integration tests for Health Check endpoints
/// </summary>
[TestFixture]
[Category("Integration")]
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
    }

    [Test]
    public async Task RootEndpoint_ShouldReturnApiInfo()
    {
        // Act
        var response = await Client.GetAsync("/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("EShop Ordering API");
        content.Should().Contain("1.0.0");
    }
}
