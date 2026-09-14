namespace EShop.Notification.Infrastructure.Configuration;

public sealed class PasswordResetSettings
{
    public const string SectionName = "PasswordReset";

    /// <summary>
    /// Where reset emails link to. Required, with no default (Notification audit S4, D4): the old
    /// <c>https://localhost:3000/reset-password</c> reached every deployed environment unnoticed.
    /// </summary>
    public string ResetUrlBase { get; set; } = string.Empty;
}
