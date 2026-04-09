namespace EShop.ApiGateway.Simulation;

public sealed record SimulationProfile(
    string RouteId,
    string PathPrefix,
    bool Enabled,
    int DelayMinMs,
    int DelayMaxMs,
    double ErrorRate,
    IReadOnlyList<string> FailureModes,
    string? ForcedFailureMode,
    string ResponseTemplate);
