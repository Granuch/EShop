using System.Net.Mail;
using EShop.BuildingBlocks.Infrastructure.Configuration;
using EShop.Notification.Infrastructure.Configuration;
using EShop.Notification.Infrastructure.Services;

namespace EShop.Notification.API.Configuration;

/// <summary>
/// Notification audit S4 (M9, M10, L6, the template part of L23; D4). Everything Notification needs to deliver email,
/// checked once while <c>Program.cs</c> composes the host, so a misconfigured deploy never starts.
///
/// <para>Before S4 these were checked in consumer constructors, message by message: a missing <c>Smtp:FromEmail</c>
/// failed every payment-failed and refund message while the other consumers ran, nothing checked the Identity URL or API
/// key, and the reset URL had two rules — the consumer accepted http, <c>Program.cs</c> demanded https outside
/// Development — neither of which noticed the tracked localhost default in every deployed environment.</para>
///
/// <para><b>The rules.</b></para>
/// <list type="bullet">
///   <item>Every environment: the Identity base URL and the reset URL are absolute http(s) URLs, <c>Smtp:Host</c> is set,
///   <c>Smtp:FromEmail</c> is an address, and every template in <see cref="NotificationTemplates.All"/> exists.</item>
///   <item>Outside Testing, which runs on EF InMemory: <c>ConnectionStrings:NotificationDb</c> is set.</item>
///   <item>Outside Development and Testing — Sandbox included, as <see cref="JwtSecretGuard"/> and Basket's guard do:
///   <c>IdentityService:ApiKey</c> is set, and no setting in <see cref="PlaceholderCheckedSettings"/> contains a
///   placeholder pattern.</item>
///   <item>Production (D4): the reset URL uses https and is not a loopback address. Sandbox and k8s are local stacks
///   whose browser reaches the storefront on localhost:3000, so they may use <c>http://localhost</c>.</item>
/// </list>
/// <para>S5 (M6, D7) added the SMTP connection: outside Development and Testing, credentials over
/// <see cref="SmtpSecurity.None"/> are refused and the SMTP credentials are placeholder-checked; Production also refuses
/// <see cref="SmtpSecurity.None"/> altogether.</para>
/// </summary>
public static class NotificationConfigurationGuard
{
    public const string ResetUrlKey = "PasswordReset:ResetUrlBase";

    /// <summary>The settings checked for a placeholder outside Development and Testing.</summary>
    public static IReadOnlyList<string> PlaceholderCheckedSettings { get; } =
    [
        "IdentityService:ApiKey", "IdentityService:BaseUrl", ResetUrlKey,
        "RabbitMQ:Host", "RabbitMQ:Username", "RabbitMQ:Password",
        "Smtp:Username", "Smtp:Password"
    ];

    /// <exception cref="InvalidOperationException">A setting is missing, malformed, or a placeholder where one is not allowed, or a template is missing.</exception>
    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var isTesting = environment.IsEnvironment("Testing");

        if (!isTesting && string.IsNullOrWhiteSpace(configuration.GetConnectionString("NotificationDb")))
        {
            throw new InvalidOperationException("ConnectionStrings:NotificationDb is required.");
        }

        RequireHttpUrl(configuration, "IdentityService:BaseUrl");
        var resetUrl = RequireHttpUrl(configuration, ResetUrlKey);
        RequireValue(configuration, "Smtp:Host");
        if (!MailAddress.TryCreate(RequireValue(configuration, "Smtp:FromEmail"), out _))
        {
            throw new InvalidOperationException("Smtp:FromEmail must be an email address.");
        }

        RequireTemplates(environment);

        // Binding also refuses an unknown Smtp:Security value, in every environment.
        var smtp = configuration.GetSection(SmtpSettings.SectionName).Get<SmtpSettings>() ?? new SmtpSettings();

        if (environment.IsDevelopment() || isTesting)
        {
            return;
        }

        RequireValue(configuration, "IdentityService:ApiKey");

        foreach (var key in PlaceholderCheckedSettings)
        {
            var value = configuration[key];
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            foreach (var pattern in JwtSecretGuard.PlaceholderPatterns)
            {
                if (value.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"{key} contains placeholder pattern '{pattern}'. Replace it with a real value before deploying to {environment.EnvironmentName}.");
                }
            }
        }

        // S5 (M6, D7). Mailpit takes plaintext without credentials; a real provider takes credentials over TLS.
        if (smtp.EffectiveSecurity == SmtpSecurity.None && !string.IsNullOrWhiteSpace(smtp.Username))
        {
            throw new InvalidOperationException(
                "Smtp:Username is set but Smtp:Security is None, so the SMTP credentials would travel in cleartext. "
                + "Use StartTls or SslOnConnect, or remove the credentials (Mailpit needs none).");
        }

        if (environment.IsProduction())
        {
            if (smtp.EffectiveSecurity == SmtpSecurity.None)
            {
                throw new InvalidOperationException("Smtp:Security must be StartTls or SslOnConnect in Production.");
            }

            if (resetUrl.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException($"{ResetUrlKey} must use HTTPS in Production.");
            }

            if (resetUrl.IsLoopback)
            {
                throw new InvalidOperationException(
                    $"{ResetUrlKey} points at {resetUrl.Host}. In Production it must be the storefront's public address.");
            }
        }
    }

    private static string RequireValue(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{key} is required.");
        }

        return value;
    }

    private static Uri RequireHttpUrl(IConfiguration configuration, string key)
    {
        // On Linux "/reset-password" parses as an absolute file: URI, so the scheme check is what rejects a path.
        if (!Uri.TryCreate(RequireValue(configuration, key), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException($"{key} must be an absolute http or https URL.");
        }

        return uri;
    }

    private static void RequireTemplates(IHostEnvironment environment)
    {
        var root = TemplateRenderer.ResolveTemplatesRoot(environment.ContentRootPath);
        var missing = NotificationTemplates.All
            .Where(name => !File.Exists(Path.Combine(root, $"{name}.html")))
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Email templates are missing from {root}: {string.Join(", ", missing)}.");
        }
    }
}
