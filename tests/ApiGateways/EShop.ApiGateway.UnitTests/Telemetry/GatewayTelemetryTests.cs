using EShop.ApiGateway.Telemetry;

namespace EShop.ApiGateway.UnitTests.Telemetry;

[TestFixture]
public sealed class GatewayTelemetryTests
{
    [Test]
    public void RecordMethods_DoNotThrow()
    {
        Assert.DoesNotThrow(() => GatewayTelemetry.RecordRequest("proxy", "route-1", 200, 12.34));
        Assert.DoesNotThrow(() => GatewayTelemetry.RecordSimulatedFailure("route-2", 503));
        Assert.DoesNotThrow(() => GatewayTelemetry.RecordRateLimited("route-3"));
        Assert.DoesNotThrow(() => GatewayTelemetry.RecordEmailQueued("RateLimitExceeded"));
        Assert.DoesNotThrow(() => GatewayTelemetry.RecordEmailSent("RateLimitExceeded", "success"));
    }
}
