using EShop.ApiGateway.Simulation;
using Microsoft.AspNetCore.Http;

namespace EShop.ApiGateway.UnitTests.Simulation;

[TestFixture]
public class SimulationResponseFactoryTests
{
    [Test]
    public async Task WriteAsync_UsesForcedFailureMode_WhenConfigured()
    {
        var factory = new SimulationResponseFactory();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var profile = new SimulationProfile(
            RouteId: "orders",
            PathPrefix: "/api/v1/orders",
            Enabled: true,
            DelayMinMs: 0,
            DelayMaxMs: 0,
            ErrorRate: 0,
            FailureModes: ["500"],
            ForcedFailureMode: "503",
            ResponseTemplate: "orders_list");

        await factory.WriteAsync(context, profile, CancellationToken.None);

        Assert.That(context.Response.StatusCode, Is.EqualTo(StatusCodes.Status503ServiceUnavailable));
    }
}
