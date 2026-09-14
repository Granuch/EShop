using EShop.Notification.Infrastructure.Configuration;
using MailKit.Security;

namespace EShop.Notification.Infrastructure.Services;

/// <summary>The one mapping from <see cref="SmtpSecurity"/> to MailKit, shared by the email sender and the health check.</summary>
public static class SmtpSecurityExtensions
{
    public static SecureSocketOptions ToSocketOptions(this SmtpSecurity security) => security switch
    {
        SmtpSecurity.None => SecureSocketOptions.None,
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        _ => throw new ArgumentOutOfRangeException(nameof(security), security, "Unknown SMTP security mode.")
    };
}
