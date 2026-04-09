using EShop.ApiGateway.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EShop.ApiGateway.UnitTests.Health;

[TestFixture]
public sealed class GatewayLivenessHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsync_ReturnsHealthy()
    {
        var check = new GatewayLivenessHealthCheck();
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Healthy));
    }
}
