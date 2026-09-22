using System.Globalization;
using EShop.Basket.Domain.Interfaces;

namespace EShop.Basket.Application.Queries.Admin;

/// <summary>
/// The <c>cursor</c> a basket walk hands back and takes again: <c>"{scanCursor}-{offset}-{fingerprint}"</c>, the last in
/// hex. Opaque to a client — pass back exactly what <c>nextCursor</c> said. The offset is what lets a page stop part-way
/// through one <c>SCAN</c> batch; without it a page either overran its size or silently dropped the rest of the batch.
/// </summary>
public static class BasketScanCursor
{
    public static string Format(BasketScanPosition position)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{position.Cursor}-{position.Offset}-{position.BatchFingerprint:x16}");

    /// <summary>An absent cursor is the start of the walk; anything else must be one this API issued.</summary>
    public static bool TryParse(string? value, out BasketScanPosition position)
    {
        position = BasketScanPosition.Start;

        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        var parts = value.Split('-');
        if (parts.Length != 3
            || !ulong.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var cursor)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var offset)
            || parts[2].Length != 16
            || !ulong.TryParse(parts[2], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var fingerprint))
        {
            return false;
        }

        position = new BasketScanPosition(cursor, offset, fingerprint);
        return true;
    }
}
