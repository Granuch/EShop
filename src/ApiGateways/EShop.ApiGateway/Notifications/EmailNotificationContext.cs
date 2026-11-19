namespace EShop.ApiGateway.Notifications;

/// <param name="SuppressedCount">
/// Notices of the same event type and route that <see cref="GatewayNoticeThrottle"/> held back since the previous one
/// was sent. Set by <see cref="EmailNotificationService"/>, never by the middleware.
/// </param>
public sealed record EmailNotificationContext(
    string EventType,
    string Route,
    int? StatusCode,
    string Path,
    string? UserId,
    string? UserEmail,
    string CorrelationId,
    DateTime OccurredAtUtc,
    int SuppressedCount = 0)
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
