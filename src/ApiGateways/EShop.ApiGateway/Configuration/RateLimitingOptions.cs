namespace EShop.ApiGateway.Configuration;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public int GlobalPermitLimit { get; set; } = 100;
    public int GlobalWindowSeconds { get; set; } = 60;
    public int SimulationPermitLimit { get; set; } = 30;
    public int SimulationWindowSeconds { get; set; } = 60;
}
