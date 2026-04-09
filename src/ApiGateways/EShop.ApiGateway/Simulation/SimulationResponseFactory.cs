using System.Text.Json;

namespace EShop.ApiGateway.Simulation;

public sealed class SimulationResponseFactory : ISimulationResponseFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private static readonly IReadOnlyDictionary<string, object> Templates = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
    {
        ["default"] = new { status = "simulated", source = "gateway" },
        ["orders_list"] = new
        {
            items = new[]
            {
                new { id = Guid.NewGuid(), status = "Pending", total = 129.90m },
                new { id = Guid.NewGuid(), status = "Created", total = 59.20m }
            }
        }
    };

    public async Task WriteAsync(HttpContext context, SimulationProfile profile, CancellationToken cancellationToken)
    {
        var delay = Random.Shared.Next(profile.DelayMinMs, profile.DelayMaxMs + 1);
        if (delay > 0)
        {
            await Task.Delay(delay, cancellationToken);
        }

        var forcedMode = profile.ForcedFailureMode;
        var shouldFail = !string.IsNullOrWhiteSpace(forcedMode)
            || Random.Shared.NextDouble() < profile.ErrorRate;

        if (shouldFail)
        {
            var mode = string.IsNullOrWhiteSpace(forcedMode)
                ? PickFailureMode(profile.FailureModes)
                : forcedMode;
            await WriteFailureAsync(context, mode, cancellationToken);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json";

        var payload = Templates.TryGetValue(profile.ResponseTemplate, out var template)
            ? template
            : Templates["default"];

        await JsonSerializer.SerializeAsync(context.Response.Body, payload, JsonOptions, cancellationToken);
    }

    private static string PickFailureMode(IReadOnlyList<string> modes)
    {
        if (modes.Count == 0)
        {
            return "500";
        }

        return modes[Random.Shared.Next(0, modes.Count)];
    }

    private static async Task WriteFailureAsync(HttpContext context, string mode, CancellationToken cancellationToken)
    {
        context.Response.ContentType = "application/json";

        if (mode.Equals("timeout", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
        }
        else if (mode.Equals("503", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        }

        var payload = new
        {
            status = "simulated-error",
            code = context.Response.StatusCode,
            mode
        };

        await JsonSerializer.SerializeAsync(context.Response.Body, payload, JsonOptions, cancellationToken);
    }
}
