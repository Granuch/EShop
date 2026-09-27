namespace EShop.BuildingBlocks.Messaging.Events;

/// <summary>
/// Integration event published when a user needs an email-confirmation link: at registration, and
/// again on <c>POST /api/v1/auth/resend-confirmation</c>. Notification turns it into the email.
///
/// <para>
/// A separate event rather than a token on <see cref="UserRegisteredIntegrationEvent"/>, for two
/// reasons. Registration is not the only occasion — a resend needs the same email without a second
/// "user registered" — and <see cref="UserRegisteredIntegrationEvent"/> is a plain fact other
/// services may consume, which should not carry a credential.
/// </para>
///
/// <para>
/// It carries a live confirmation token, so it is marked <see cref="ISensitivePayloadEvent"/>:
/// the outbox row's payload is redacted as soon as the event is dispatched, as for
/// <see cref="PasswordResetRequestedIntegrationEvent"/>. The address is left out, as on every
/// Identity event; Notification resolves it from <c>UserId</c>.
/// </para>
/// </summary>
public sealed record EmailConfirmationRequestedIntegrationEvent : IntegrationEvent, ISensitivePayloadEvent
{
    public string UserId { get; init; } = string.Empty;
    public string ConfirmationToken { get; init; } = string.Empty;
}
