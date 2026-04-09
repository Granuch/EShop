using System.Net;
using EShop.ApiGateway.IntegrationTests.Fixtures;

namespace EShop.ApiGateway.IntegrationTests.Health;

[TestFixture]
public sealed class GatewayHealthEndpointTests
{
    [Test]
    public async Task LivenessEndpoint_ShouldReturnHealthy()
    {
        await using var factory = new GatewayApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");
        var content = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(content, Does.Contain("Healthy"));
    }

    [Test]
    public async Task ReadinessEndpoint_ShouldReturnHealthyOrDegradedInTesting()
    {
        await using var factory = new GatewayApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");
        var content = await response.Content.ReadAsStringAsync();

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(content.Contains("Healthy") || content.Contains("Degraded"), Is.True);
    }
}
