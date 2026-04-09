namespace EShop.ApiGateway.Notifications;

public sealed record EmailNotificationContext(
    string EventType,
    string Route,
    int? StatusCode,
    string Path,
    string? UserId,
    string? UserEmail,
    string CorrelationId,
    DateTime OccurredAtUtc)
{
    public static EmailNotificationContext ForSimulation(
        string route,
        int statusCode,
        string path,
        string? userId,
        string correlationId)
    {
        return new EmailNotificationContext(
            EventType: "SimulationResponse",
            Route: route,
            StatusCode: statusCode,
            Path: path,
            UserId: userId,
            UserEmail: null,
            CorrelationId: correlationId,
            OccurredAtUtc: DateTime.UtcNow);
    }
}
