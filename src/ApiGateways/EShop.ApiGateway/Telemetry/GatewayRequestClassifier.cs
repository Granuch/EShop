namespace EShop.ApiGateway.Telemetry;

public static class GatewayRequestClassifier
{
    public static string GetRequestMode(bool isSimulation) => isSimulation ? "simulation" : "proxy";

    public static bool IsRateLimitStatus(int statusCode) => statusCode == StatusCodes.Status429TooManyRequests;

    public static bool IsServerFailureStatus(int statusCode) => statusCode >= StatusCodes.Status500InternalServerError;
}
