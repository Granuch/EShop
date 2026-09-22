using EShop.BuildingBlocks.Infrastructure.Auditing;

namespace EShop.ApiGateway.AuditLog;

/// <summary>
/// Merges the services' audit pages into one newest-first page, and works out where each service resumes.
///
/// <para>
/// <b>It is a k-way merge on the lists' heads, not a sort of their union, and that is load-bearing.</b> Each service
/// returns its rows in id order, which is only <i>nearly</i> time order: two writers in one service can commit a
/// later-stamped row under the smaller id. Sorting the union by time could then take a service's third row and leave
/// its second, and the cursor — "stay below the last id taken" — would skip the second for good. Always taking the
/// newest <i>head</i> keeps what is taken from each service a prefix of its list, so no row is ever skipped or
/// repeated; the price is that the merged page is only as time-ordered as the services' own lists.
/// </para>
/// </summary>
public static class AuditLogMerge
{
    /// <param name="pages">
    /// Every service queried for this page, with the page it returned, or <c>null</c> when it could not be read. A
    /// service that could not be read keeps its old cursor position, so its rows are not lost — they arrive on a later
    /// page instead.
    /// </param>
    public static (IReadOnlyList<AuditLogEntryDto> Items, AuditLogCursor Next) Merge(
        int pageSize,
        AuditLogCursor cursor,
        IReadOnlyDictionary<string, AuditLogPageDto?> pages)
    {
        var readable = pages
            .Where(p => p.Value is not null)
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => (Service: p.Key, Page: p.Value!))
            .ToList();

        var taken = readable.ToDictionary(r => r.Service, _ => 0, StringComparer.Ordinal);
        var items = new List<AuditLogEntryDto>(pageSize);

        while (items.Count < pageSize)
        {
            (string Service, AuditLogEntryDto Head)? newest = null;
            foreach (var (service, page) in readable)
            {
                if (taken[service] >= page.Items.Count)
                {
                    continue;
                }

                var head = page.Items[taken[service]];
                // Ties go to the service that sorts first, so a page is deterministic.
                if (newest is null || head.OccurredAt > newest.Value.Head.OccurredAt)
                {
                    newest = (service, head);
                }
            }

            if (newest is null)
            {
                break;
            }

            items.Add(newest.Value.Head);
            taken[newest.Value.Service]++;
        }

        var before = new Dictionary<string, long>(cursor.Before, StringComparer.Ordinal);
        var exhausted = new HashSet<string>(cursor.Exhausted, StringComparer.Ordinal);

        foreach (var (service, page) in readable)
        {
            var count = taken[service];
            if (count > 0)
            {
                before[service] = page.Items[count - 1].Id;
            }

            if (count == page.Items.Count && page.NextBefore is null)
            {
                // Everything this service had left was taken: nothing further to ask it for.
                before.Remove(service);
                exhausted.Add(service);
            }
        }

        return (items, new AuditLogCursor(before, exhausted));
    }
}
