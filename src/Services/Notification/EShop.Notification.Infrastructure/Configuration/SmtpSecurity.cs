namespace EShop.Notification.Infrastructure.Configuration;

/// <summary>
/// How the SMTP connection is secured (Notification audit S5, D7). It replaces <c>Smtp:UseSsl</c>, which could only mean
/// STARTTLS or plaintext, so an implicit-TLS server (port 465) could not be used at all.
/// </summary>
public enum SmtpSecurity
{
    /// <summary>Plaintext. Only for a local relay such as Mailpit, and never with credentials outside Development.</summary>
    None,

    /// <summary>Connect in plaintext, then upgrade with STARTTLS, which the server must offer (usually port 587).</summary>
    StartTls,

    /// <summary>TLS from the first byte (implicit TLS, usually port 465).</summary>
    SslOnConnect
}
