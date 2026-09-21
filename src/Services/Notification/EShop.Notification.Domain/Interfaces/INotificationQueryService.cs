using EShop.Notification.Domain.Entities;

namespace EShop.Notification.Domain.Interfaces;

/// <summary>
/// What the admin journal narrows on (Admin panel S12, endpoints #70/#71/#77). One record, so the list and the stats
/// read cannot disagree about what an omitted bound or a blank filter means.
/// </summary>
/// <param name="Statuses">A union: <c>?status=Failed&amp;status=Undeliverable</c> answers both. Empty means every status.</param>
/// <param name="EventType">Case-insensitive exact match on the integration event's type name.</param>
/// <param name="TemplateName">Case-insensitive exact match on the template.</param>
/// <param name="UserId">Exact match on the recipient's user id.</param>
/// <param name="RecipientEmail">Case-insensitive exact match on the address the email went to.</param>
/// <param name="From">Inclusive lower bound on <c>CreatedAt</c>. Already coerced to UTC — see <c>NotificationQueryEnums.AsUtc</c>.</param>
/// <param name="To">Inclusive upper bound on <c>CreatedAt</c>, coerced the same way.</param>
/// <param name="HasError">True: only rows carrying a <c>LastError</c>. False: only rows carrying none. Null: both.</param>
public sealed record NotificationJournalFilter(
    NotificationStatus[] Statuses,
    string? EventType,
    string? TemplateName,
    string? UserId,
    string? RecipientEmail,
    DateTime? From,
    DateTime? To,
    bool? HasError)
{
    /// <summary>Everything, unfiltered.</summary>
    public static NotificationJournalFilter All { get; } = new([], null, null, null, null, null, null, null);
}

/// <summary>How many notifications are in one status.</summary>
public sealed record NotificationStatusCount(NotificationStatus Status, int Count);

/// <summary>
/// The journal dashboard's numbers over one window (#77).
/// </summary>
/// <remarks>
/// <see cref="Total"/> and the three named figures are summed in memory from <see cref="ByStatus"/> rather than queried
/// again, so they cannot disagree with the breakdown under a concurrent delivery. Payment's <c>PaymentStats</c> is the
/// same shape for the same reason.
/// </remarks>
public sealed record NotificationJournalStats(
    DateTime? From,
    DateTime? To,
    int Total,
    int Sent,
    int Failed,
    int Queued,
    int Undeliverable,
    IReadOnlyList<NotificationStatusCount> ByStatus);

/// <summary>
/// The read-model side of Notification (Admin panel S12), kept apart from <see cref="Application"/>-owned
/// <c>INotificationLogRepository</c> on purpose: the repository is the delivery path's write seam, its reads are
/// tracked and its <c>SaveAsync</c> races on the row version, while everything here is <c>AsNoTracking</c> and
/// answers a screen.
/// <para>The interface lives in Domain because Application calls it and Infrastructure owns it — the repo's rule for
/// every cross-layer call, and where <c>IEmailService</c> already sits.</para>
/// </summary>
public interface INotificationQueryService
{
    /// <summary>
    /// One OFFSET page of the journal, plus the total under the same filter. Newest first, with <c>Id</c> breaking
    /// ties — offset paging over a non-unique sort is nondeterministic on Postgres, so a notification could appear on
    /// two pages or on none.
    /// </summary>
    Task<(List<NotificationLog> Items, int TotalCount)> GetPageAsync(
        NotificationJournalFilter filter,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>One notification, or null. Returns the entity, so the detail endpoint projects the same fields the list does.</summary>
    Task<NotificationLog?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The breakdown over <paramref name="window"/>, in one <c>GROUP BY "Status"</c>.</summary>
    Task<NotificationJournalStats> GetStatsAsync(
        NotificationJournalFilter window,
        CancellationToken cancellationToken = default);
}
