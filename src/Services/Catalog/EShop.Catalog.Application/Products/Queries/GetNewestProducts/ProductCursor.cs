using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace EShop.Catalog.Application.Products.Queries.GetNewestProducts;

/// <summary>
/// H4. The position of the last product a client has seen in newest-first order.
///
/// <para>
/// <b>Both halves are required.</b> The cursor used to be a bare <c>CreatedAt</c>, compared with
/// <c>CreatedAt &lt; cursor</c>. <c>CreatedAt</c> is not unique — seeded and bulk-imported rows
/// share timestamps — so every product tied with the last row of a page was silently skipped.
/// <c>(CreatedAt, Id)</c> is unique, so the ordering is total and no row can fall between pages.
/// </para>
///
/// <para>
/// Encoded opaquely (base64url) so clients pass it back unchanged rather than constructing one;
/// that keeps the format ours to change. Postgres stores microseconds and the cursor is built from
/// a value read back from it, so the ticks round-trip exactly.
/// </para>
/// </summary>
public readonly record struct ProductCursor(DateTime CreatedAt, Guid Id)
{
    // ticks (<= 19 digits) + ':' + 32 hex digits = 52 bytes, ~70 chars encoded. Anything much
    // longer is not a cursor we issued, and bounding it bounds the work of rejecting it.
    private const int MaxEncodedLength = 128;

    public string Encode()
    {
        // A Local value would encode a different instant from the one stored; Unspecified is how
        // a timestamptz round-trips through some serializers and is treated as the UTC it came from.
        var utc = CreatedAt.Kind == DateTimeKind.Local ? CreatedAt.ToUniversalTime() : CreatedAt;
        var raw = string.Create(CultureInfo.InvariantCulture, $"{utc.Ticks}:{Id:N}");
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(raw));
    }

    public static bool TryDecode(string? value, out ProductCursor cursor)
    {
        cursor = default;

        if (string.IsNullOrEmpty(value) || value.Length > MaxEncodedLength || !Base64Url.IsValid(value))
            return false;

        var raw = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(value));
        var separator = raw.IndexOf(':');
        if (separator <= 0)
            return false;

        if (!long.TryParse(raw.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks > DateTime.MaxValue.Ticks)
            return false;

        if (!Guid.TryParseExact(raw.AsSpan(separator + 1), "N", out var id))
            return false;

        cursor = new ProductCursor(new DateTime(ticks, DateTimeKind.Utc), id);
        return true;
    }

    /// <summary>
    /// Decodes a cursor the validator has already accepted. Throws rather than returning
    /// "no cursor" on garbage: silently serving the first page for a cursor we could not read is
    /// exactly the H4 defect, and a bypassed validator should fail loudly, not quietly.
    /// </summary>
    public static ProductCursor Decode(string value)
        => TryDecode(value, out var cursor)
            ? cursor
            : throw new FormatException("Not a valid product cursor.");
}
