namespace EShop.Notification.Domain.Models;

public sealed class PasswordResetEmailModel
{
    public string CustomerName { get; init; } = string.Empty;
    public string ResetLink { get; init; } = string.Empty;
}
