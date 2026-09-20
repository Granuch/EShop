using EShop.Payment.Domain.Entities;
using EShop.Payment.Domain.Interfaces;

namespace EShop.Payment.Application.Payments.Queries;

/// <summary>
/// The conversions every admin read query needs between what arrives on the query string and what
/// <see cref="IPaymentQueryService"/> takes (Admin panel S10). Shared so the list, the export and the stats read
/// cannot disagree about what a name, an omitted bound or a missing currency means. Ordering's <c>QueryEnums</c> is
/// the same class for the same reason.
/// </summary>
internal static class PaymentQueryEnums
{
    /// <summary>The currency every figure in the stats response is in when the caller names none. Every payment
    /// Payment writes is USD (audit D4), so this is the only value that exists today.</summary>
    public const string DefaultCurrency = "USD";

    /// <summary>
    /// The statuses the caller asked for. The validator has already rejected anything that is not a status name, so
    /// <c>Enum.Parse</c> here cannot fail — a <c>TryParse</c> with a fallback would answer a typo with every payment.
    /// </summary>
    public static PaymentStatus[] ParseStatuses(IEnumerable<string>? names)
        => names?
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => Enum.Parse<PaymentStatus>(n.Trim(), ignoreCase: true))
            .Distinct()
            .ToArray() ?? [];

    /// <inheritdoc cref="ParseStatuses"/>
    public static PaymentMethodType? ParseMethod(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : Enum.Parse<PaymentMethodType>(value.Trim(), ignoreCase: true);

    /// <summary>
    /// The bucket size, or <see cref="PaymentStatsGroupBy.Day"/>. The validator rejects an unknown name before this
    /// runs, so the fallback is for the omitted case, not for a typo.
    /// </summary>
    public static PaymentStatsGroupBy ParseGroupBy(string? value)
        => Enum.TryParse<PaymentStatsGroupBy>(value, ignoreCase: true, out var parsed)
            ? parsed
            : PaymentStatsGroupBy.Day;

    /// <summary>
    /// Reads a bound date as UTC.
    ///
    /// <para>
    /// <b>Not cosmetic.</b> <c>?from=2026-09-01</c> — what an admin URL actually looks like — parses to a
    /// <see cref="DateTime"/> with <see cref="DateTimeKind.Unspecified"/>, and Npgsql refuses to send one as a
    /// <c>timestamp with time zone</c> parameter. Without this the most obvious filter on the screen is a 500 rather
    /// than a filter, and only a value carrying <c>Z</c> or an offset works — a rule no caller can guess from the URL.
    /// Ordering's <c>QueryEnums.AsUtc</c> is the same function; Catalog's and Identity's date filters still lack it.
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
