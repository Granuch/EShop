namespace EShop.Notification.Infrastructure.Configuration;

public sealed class SmtpSettings
{
    public const string SectionName = "Smtp";

    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 587;
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public bool UseSsl { get; init; } = true;
    public string FromEmail { get; init; } = string.Empty;
    public string FromName { get; init; } = string.Empty;

    /// <summary>
    /// Controls TLS certificate revocation checking. Defaults to true (enabled).
    /// Set to false only for local development environments where no CRL/OCSP is available.
    /// </summary>
    public bool CheckCertificateRevocation { get; init; } = true;
}
