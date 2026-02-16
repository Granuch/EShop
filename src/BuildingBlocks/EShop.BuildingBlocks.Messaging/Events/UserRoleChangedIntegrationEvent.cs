namespace EShop.BuildingBlocks.Messaging.Events;

/// <summary>
/// Integration event published when a user's role is changed.
/// Note: Email intentionally omitted to avoid PII in message payloads.
/// </summary>
public record UserRoleChangedIntegrationEvent : IntegrationEvent
{
    public string UserId { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public bool IsAssigned { get; init; }
}
