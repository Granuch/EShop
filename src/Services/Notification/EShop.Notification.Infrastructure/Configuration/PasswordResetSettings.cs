namespace EShop.Notification.Infrastructure.Configuration;

public sealed class PasswordResetSettings
{
    public const string SectionName = "PasswordReset";

    public string ResetUrlBase { get; set; } = "http://localhost:3000/reset-password";
}
