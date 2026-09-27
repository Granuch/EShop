namespace EShop.Notification.Infrastructure.Configuration;

public sealed class EmailConfirmationSettings
{
    public const string SectionName = "EmailConfirmation";

    /// <summary>
    /// The storefront page confirmation emails link to. Required, with no tracked default — the same rule as
    /// <see cref="PasswordResetSettings.ResetUrlBase"/>, for the same reason: a localhost default would reach every
    /// deployed environment unnoticed. Checked at startup by <c>NotificationConfigurationGuard</c>.
    /// </summary>
    public string ConfirmUrlBase { get; set; } = string.Empty;
}
