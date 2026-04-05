using System.Net.Mail;

namespace EShop.Notification.Domain.ValueObjects;

public sealed record RecipientAddress
{
    public RecipientAddress(string email, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email is required.", nameof(email));
        }

        try
        {
            var parsed = new MailAddress(email);
            Email = parsed.Address;
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("Email format is invalid.", nameof(email), ex);
        }

        DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
    }

    public string Email { get; }
    public string? DisplayName { get; }
}
