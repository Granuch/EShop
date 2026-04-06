using EShop.ApiGateway.Simulation;
using EShop.ApiGateway.Telemetry;

namespace EShop.ApiGateway.Middleware;

public sealed class SimulationResponseMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ISimulationResponseFactory _simulationResponseFactory;

    public SimulationResponseMiddleware(
        RequestDelegate next,
        ISimulationResponseFactory simulationResponseFactory)
    {
        _next = next;
        _simulationResponseFactory = simulationResponseFactory;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var enabled = context.Items.TryGetValue(SimulationContextKeys.Enabled, out var enabledObj)
            && enabledObj is bool value
            && value;

        if (!enabled)
        {
            await _next(context);
            return;
        }

        if (!context.Items.TryGetValue(SimulationContextKeys.Profile, out var profileObj) || profileObj is not SimulationProfile profile)
        {
            await _next(context);
            return;
        }

        using var activity = GatewayActivitySource.Instance.StartActivity("gateway.simulation.respond");
        activity?.SetTag("route", profile.RouteId);
        activity?.SetTag("path", context.Request.Path.ToString());
        activity?.SetTag("response_template", profile.ResponseTemplate);

        await _simulationResponseFactory.WriteAsync(context, profile, context.RequestAborted);

        activity?.SetTag("status_code", context.Response.StatusCode);
    }
}
