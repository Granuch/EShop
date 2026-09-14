namespace EShop.BuildingBlocks.Messaging.Events;

/// <summary>
/// Integration event published when a user requests password reset.
///
/// Carries a live password-reset token, so it is marked <see cref="ISensitivePayloadEvent"/>:
/// the outbox row's payload is redacted as soon as the event is dispatched rather than being
/// retained in plaintext for the outbox retention window.
/// </summary>
public sealed record PasswordResetRequestedIntegrationEvent : IntegrationEvent, ISensitivePayloadEvent
{
    public string UserId { get; init; } = string.Empty;
    public string ResetToken { get; init; } = string.Empty;
}
