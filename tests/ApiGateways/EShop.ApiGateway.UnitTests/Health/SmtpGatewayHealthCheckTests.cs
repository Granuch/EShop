using EShop.ApiGateway.Configuration;
using EShop.ApiGateway.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.UnitTests.Health;

[TestFixture]
public sealed class SmtpGatewayHealthCheckTests
{
    [Test]
    public async Task CheckHealthAsync_ReturnsDegraded_WhenHostIsNotConfigured()
    {
        var options = Options.Create(new EmailOptions
        {
            Host = string.Empty,
            Port = 25,
            UseSsl = false
        });

        var check = new SmtpGatewayHealthCheck(options);
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Degraded));
    }
}
