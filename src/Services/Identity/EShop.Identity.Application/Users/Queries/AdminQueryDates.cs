namespace EShop.Identity.Application.Users.Queries;

/// <summary>
/// How the admin user reads take a date from the query string (frontend-contracts F-15).
/// </summary>
internal static class AdminQueryDates
{
    /// <summary>
    /// Reads a bound date as UTC.
    ///
    /// <para>
    /// <b>Not cosmetic.</b> <c>?createdFrom=2026-09-01</c> binds to a <see cref="DateTime"/> with
    /// <see cref="DateTimeKind.Unspecified"/>, and Npgsql refuses to send one as a
    /// <c>timestamp with time zone</c> parameter, so the list and the stats tile answered 500 for any
    /// date a caller typed without <c>Z</c> or an offset. An offset binds as
    /// <see cref="DateTimeKind.Local"/> and is converted. Ordering's <c>QueryEnums.AsUtc</c> is the
    /// same function.
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
