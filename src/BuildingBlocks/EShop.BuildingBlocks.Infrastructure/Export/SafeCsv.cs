using System.Globalization;
using System.Text;

namespace EShop.BuildingBlocks.Infrastructure.Export;

/// <summary>
/// The field rules every CSV export in the platform follows: RFC 4180 quoting plus spreadsheet-formula neutralisation.
///
/// <para>
/// Extracted from Payment's accounting export when Catalog gained a product export (admin panel S16). Two copies of the
/// injection defence would drift — a character added to one list and not the other is a live formula in exactly one of
/// the two files an operator opens — so both writers call this and only decide their columns.
/// </para>
/// </summary>
public static class SafeCsv
{
    /// <summary>The line terminator RFC 4180 specifies, and the one Excel expects.</summary>
    public const string LineEnd = "\r\n";

    /// <summary>
    /// One field, quoted per RFC 4180 and made inert for a spreadsheet. <c>null</c> and empty are an empty field.
    ///
    /// <para><b>Both halves matter and neither is cosmetic.</b> Quoting is needed because exported fields carry real
    /// free text — a Stripe decline reason, an operator's refund note, a product description — and a comma or a quote
    /// in one would otherwise shift every later column of that row silently. The leading apostrophe is CSV injection
    /// defence: a field opening with <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, tab or carriage return is treated by Excel
    /// and Sheets as a <i>formula</i>, so a value typed as <c>=cmd|…</c> becomes code the reader's spreadsheet offers to
    /// run. Quoting alone does not stop that — Excel evaluates the contents of a quoted field too.</para>
    /// </summary>
    public static string Field(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var text = value;
        if (text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            text = "'" + text;
        }

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>
    /// A money or quantity value in the invariant culture, always. A machine writing <c>1234,56</c> on a comma-separated
    /// line produces a file that parses into the wrong number of columns on a German-locale server.
    /// </summary>
    public static string Decimal(decimal? value) => value?.ToString("0.00", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>Round-trip UTC, so a spreadsheet import cannot reinterpret an instant in its own time zone.</summary>
    public static string Timestamp(DateTime? value)
        => value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>
    /// The file's bytes: UTF-8 <b>with</b> a byte-order mark. Without it Excel reads the file as the machine's ANSI code
    /// page, and any non-ASCII text arrives mangled.
    /// </summary>
    public static byte[] Encode(string csv)
        => Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
}
