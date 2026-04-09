using EShop.ApiGateway.Configuration;
using EShop.ApiGateway.Middleware;
using EShop.ApiGateway.Simulation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.UnitTests.Simulation;

[TestFixture]
public class SimulationDecisionMiddlewareTests
{
    [Test]
    public async Task InvokeAsync_DisablesSimulation_WhenGlobalFlagOff()
    {
        var provider = BuildProvider(enabled: false);
        var options = Options.Create(new SimulationOptions { Enabled = false });
        var middleware = new SimulationDecisionMiddleware(_ => Task.CompletedTask, provider, options);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/orders";

        await middleware.InvokeAsync(context);

        Assert.That(context.Items[SimulationContextKeys.Enabled], Is.EqualTo(false));
        Assert.That(context.Items[SimulationContextKeys.DecisionReason], Is.EqualTo("disabled"));
    }

    [Test]
    public async Task InvokeAsync_EnablesSimulation_WhenProfileMatches()
    {
        var provider = BuildProvider(enabled: true);
        var options = Options.Create(new SimulationOptions { Enabled = true, AllowHeaderOverride = true });
        var middleware = new SimulationDecisionMiddleware(_ => Task.CompletedTask, provider, options);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/orders/1";

        await middleware.InvokeAsync(context);

        Assert.That(context.Items[SimulationContextKeys.Enabled], Is.EqualTo(true));
        Assert.That(context.Items.ContainsKey(SimulationContextKeys.Profile), Is.True);
    }

    private static ISimulationProfileProvider BuildProvider(bool enabled)
    {
        var options = Options.Create(new SimulationOptions
        {
            Enabled = enabled,
            Routes = new Dictionary<string, SimulationRouteOptions>
            {
                ["orders"] = new()
                {
                    PathPrefix = "/api/v1/orders",
                    DelayMs = new DelayRangeOptions { Min = 1, Max = 2 },
                    ErrorRate = 0
                }
            }
        });

        return new SimulationProfileProvider(options);
    }
}
