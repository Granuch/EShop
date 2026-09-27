using EShop.Ordering.Application.Abstractions;

namespace EShop.Ordering.Application.Orders.Queries;

/// <summary>
/// The two conversions every admin read query needs between what arrives on the query string and
/// what the query service takes (Admin panel S8). Shared so the list and the stats read cannot
/// disagree about what an omitted bound or an unrecognised name means.
/// </summary>
internal static class QueryEnums
{
    /// <summary>
    /// The sort column, or <see cref="OrderSortBy.CreatedAt"/>. The validator rejects an unknown name
    /// before this runs, so the fallback is for the omitted case, not for a typo.
    /// </summary>
    public static OrderSortBy ParseSortBy(string? value)
        => Enum.TryParse<OrderSortBy>(value, ignoreCase: true, out var parsed) ? parsed : OrderSortBy.CreatedAt;

    /// <inheritdoc cref="ParseSortBy"/>
    public static OrderStatsGroupBy ParseGroupBy(string? value)
        => Enum.TryParse<OrderStatsGroupBy>(value, ignoreCase: true, out var parsed) ? parsed : OrderStatsGroupBy.Day;

    /// <summary>
    /// Reads a bound date as UTC.
    ///
    /// <para>
    /// <b>Not cosmetic.</b> <c>?from=2026-09-01</c> parses to a <see cref="DateTime"/> with
    /// <see cref="DateTimeKind.Unspecified"/>, and Npgsql refuses to send one as a
    /// <c>timestamp with time zone</c> parameter — so the filter every admin screen sends by hand
    /// would be a 500 rather than a filter. Only a value carrying <c>Z</c> or an offset works
    /// otherwise, which is a rule no caller can guess from the URL.
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
