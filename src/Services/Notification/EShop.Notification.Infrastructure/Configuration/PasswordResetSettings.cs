namespace EShop.Notification.Infrastructure.Configuration;

public sealed class PasswordResetSettings
{
    public const string SectionName = "PasswordReset";

    public string ResetUrlBase { get; set; } = "https://localhost:3000/reset-password";
}
