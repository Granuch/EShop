namespace EShop.Notification.Domain.Models;

public sealed class EmailConfirmationEmailModel
{
    public string CustomerName { get; init; } = string.Empty;
    public string ConfirmationLink { get; init; } = string.Empty;
}
