namespace EShop.ApiGateway.Configuration;

public sealed class SimulationOptions
{
    public const string SectionName = "Simulation";

    public bool Enabled { get; set; }
    public bool AllowHeaderOverride { get; set; }
    public Dictionary<string, SimulationRouteOptions> Routes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SimulationRouteOptions
{
    public string PathPrefix { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DelayRangeOptions DelayMs { get; set; } = new();
    public double ErrorRate { get; set; }
    public string[] FailureModes { get; set; } = [];
    public string? ForcedFailureMode { get; set; }
    public string ResponseTemplate { get; set; } = "default";
}

public sealed class DelayRangeOptions
{
    public int Min { get; set; }
    public int Max { get; set; }
}
