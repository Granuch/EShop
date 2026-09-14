namespace EShop.Notification.Infrastructure.Configuration;

public sealed class SmtpSettings
{
    public const string SectionName = "Smtp";

    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 587;
    public string Username { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    /// <summary>How the connection is secured (Notification audit S5, D7). When unset, <see cref="UseSsl"/> decides.</summary>
    public SmtpSecurity? Security { get; init; }

    /// <summary>
    /// Superseded by <see cref="Security"/>, and still honoured when <see cref="Security"/> is unset: true means STARTTLS,
    /// false plaintext. Kept so a configuration that still sets it — a developer's local settings file — keeps working.
    /// </summary>
    public bool UseSsl { get; init; } = true;

    /// <summary>The mode the connection actually uses.</summary>
    public SmtpSecurity EffectiveSecurity => Security ?? (UseSsl ? SmtpSecurity.StartTls : SmtpSecurity.None);
    public string FromEmail { get; init; } = string.Empty;
    public string FromName { get; init; } = string.Empty;

    /// <summary>
    /// Controls TLS certificate revocation checking. Defaults to true (enabled).
    /// Set to false only for local development environments where no CRL/OCSP is available.
    /// </summary>
    public bool CheckCertificateRevocation { get; init; } = true;
}
