namespace EShop.Payment.Infrastructure.Configuration;

public sealed class StripeSettings
{
    public const string SectionName = "Stripe";

    public bool Enabled { get; init; }
    public bool SkipWebhookSignatureVerification { get; init; }
    public string SecretKey { get; init; } = string.Empty;
    public string PublishableKey { get; init; } = string.Empty;
    public string WebhookSecret { get; init; } = string.Empty;
}
