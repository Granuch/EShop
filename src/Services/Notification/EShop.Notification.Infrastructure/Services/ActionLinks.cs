namespace EShop.Notification.Infrastructure.Services;

/// <summary>
/// The links in the account emails: <c>&lt;base&gt;?userId=…&amp;token=…</c>, both values URL-encoded, which the
/// storefront page reads back with <c>URLSearchParams</c> (which decodes them) and posts to Identity.
///
/// <para>
/// One builder for the password-reset and email-confirmation links, and for their test-send samples, because the
/// frontend contract is the same for both pages: a divergence (one side forgetting to encode the token, which carries
/// <c>+</c>, <c>/</c> and <c>=</c>) would break exactly one of the two flows and nothing else.
/// </para>
/// </summary>
public static class ActionLinks
{
    public static string WithUserAndToken(string baseUrl, string userId, string token)
    {
        var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{baseUrl}{separator}userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}";
    }
}
