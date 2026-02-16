namespace EShop.BuildingBlocks.Messaging.Events;

/// <summary>
/// Integration event published when a user confirms their email address.
/// Note: Email intentionally omitted to avoid PII in message payloads.
/// </summary>
public record UserEmailConfirmedIntegrationEvent : IntegrationEvent
{
    public string UserId { get; init; } = string.Empty;
}
