using EShop.ApiGateway.Configuration;
using EShop.ApiGateway.Simulation;
using Microsoft.Extensions.Options;

namespace EShop.ApiGateway.Middleware;

public sealed class SimulationDecisionMiddleware
{
    public const string SimulationHeaderName = "X-Simulate";

    private readonly RequestDelegate _next;
    private readonly ISimulationProfileProvider _profileProvider;
    private readonly SimulationOptions _options;

    public SimulationDecisionMiddleware(
        RequestDelegate next,
        ISimulationProfileProvider profileProvider,
        IOptions<SimulationOptions> options)
    {
        _next = next;
        _profileProvider = profileProvider;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled)
        {
            context.Items[SimulationContextKeys.Enabled] = false;
            context.Items[SimulationContextKeys.DecisionReason] = "disabled";
            await _next(context);
            return;
        }

        var headerRequested = _options.AllowHeaderOverride
            && context.Request.Headers.TryGetValue(SimulationHeaderName, out var headerValue)
            && bool.TryParse(headerValue, out var headerEnabled)
            && headerEnabled;

        var hasProfile = _profileProvider.TryGetByPath(context.Request.Path, out var profile);

        var shouldSimulate = hasProfile
            && profile.Enabled
            && (headerRequested || !context.Request.Headers.ContainsKey(SimulationHeaderName));

        context.Items[SimulationContextKeys.Enabled] = shouldSimulate;
        context.Items[SimulationContextKeys.DecisionReason] = shouldSimulate ? "matched-profile" : "no-profile";

        if (shouldSimulate)
        {
            context.Items[SimulationContextKeys.Profile] = profile;
        }

        await _next(context);
    }
}
