namespace EShop.BuildingBlocks.Messaging.Events;

/// <summary>
/// Integration event published when a user requests password reset.
/// </summary>
public sealed record PasswordResetRequestedIntegrationEvent : IntegrationEvent
{
    public string UserId { get; init; } = string.Empty;
    public string ResetToken { get; init; } = string.Empty;
}
