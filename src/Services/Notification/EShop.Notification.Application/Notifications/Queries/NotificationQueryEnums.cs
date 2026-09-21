using EShop.Notification.Domain.Entities;

namespace EShop.Notification.Application.Notifications.Queries;

/// <summary>
/// The conversions every journal read needs between what arrives on the query string and what
/// <c>INotificationQueryService</c> takes (Admin panel S12). Shared so the list and the stats read cannot disagree
/// about what a name or an omitted bound means. Payment's <c>PaymentQueryEnums</c> and Ordering's <c>QueryEnums</c>
/// are the same class for the same reason.
/// </summary>
internal static class NotificationQueryEnums
{
    /// <summary>
    /// The statuses the caller asked for. The validator has already rejected anything that is not a status name, so
    /// <c>Enum.Parse</c> here cannot fail — a <c>TryParse</c> with a fallback would answer a typo with every row.
    /// </summary>
    public static NotificationStatus[] ParseStatuses(IEnumerable<string>? names)
        => names?
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => Enum.Parse<NotificationStatus>(n.Trim(), ignoreCase: true))
            .Distinct()
            .ToArray() ?? [];

    /// <summary>
    /// Reads a bound date as UTC.
    ///
    /// <para>
    /// <b>Not cosmetic.</b> <c>?from=2026-09-01</c> — what an admin URL actually looks like — parses to a
    /// <see cref="DateTime"/> with <see cref="DateTimeKind.Unspecified"/>, and Npgsql refuses to send one as a
    /// <c>timestamp with time zone</c> parameter. Without this the most obvious filter on the screen is a 500 rather
    /// than a filter, and only a value carrying <c>Z</c> or an offset works — a rule no caller can guess from the URL.
    /// The HTTP suite runs on EF InMemory, where a <c>DateTime</c> comparison ignores <c>Kind</c> entirely and cannot
    /// show this; <c>Persistence/NotificationJournalSqlTests</c> is the half that can.
    /// </para>
    /// </summary>
    public static DateTime? AsUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } utc => utc,
        { Kind: DateTimeKind.Local } local => local.ToUniversalTime(),
        var unspecified => DateTime.SpecifyKind(unspecified.Value, DateTimeKind.Utc)
    };
}
