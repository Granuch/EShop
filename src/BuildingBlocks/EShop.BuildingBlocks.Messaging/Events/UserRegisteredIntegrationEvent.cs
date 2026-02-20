namespace EShop.BuildingBlocks.Messaging.Events;

/// <summary>
/// Integration event published when a new user registers.
/// Consumed by other services that need to react to user creation.
/// Note: Email/FullName intentionally omitted to avoid PII in message payloads.
/// Consumers should call Identity API if user details are needed.
/// </summary>
public record UserRegisteredIntegrationEvent : IntegrationEvent
{
    public string UserId { get; init; } = string.Empty;
}
