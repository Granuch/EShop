using EShop.ApiGateway.Simulation;
using Microsoft.AspNetCore.Http;
using System.Text.Json;

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

    [Test]
    public async Task WriteAsync_OrdersTemplate_ShouldGenerateDifferentIdsPerRequest()
    {
        var factory = new SimulationResponseFactory();

        var profile = new SimulationProfile(
            RouteId: "orders",
            PathPrefix: "/api/v1/orders",
            Enabled: true,
            DelayMinMs: 0,
            DelayMaxMs: 0,
            ErrorRate: 0,
            FailureModes: ["500"],
            ForcedFailureMode: null,
            ResponseTemplate: "orders_list");

        var firstContext = new DefaultHttpContext();
        firstContext.Response.Body = new MemoryStream();
        await factory.WriteAsync(firstContext, profile, CancellationToken.None);
        firstContext.Response.Body.Position = 0;
        using var firstJson = await JsonDocument.ParseAsync(firstContext.Response.Body);

        var secondContext = new DefaultHttpContext();
        secondContext.Response.Body = new MemoryStream();
        await factory.WriteAsync(secondContext, profile, CancellationToken.None);
        secondContext.Response.Body.Position = 0;
        using var secondJson = await JsonDocument.ParseAsync(secondContext.Response.Body);

        var firstId = firstJson.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid();
        var secondId = secondJson.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid();

        Assert.That(firstId, Is.Not.EqualTo(secondId));
    }
}
