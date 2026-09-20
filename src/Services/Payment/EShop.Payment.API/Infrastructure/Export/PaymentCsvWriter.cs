using System.Globalization;
using System.Text;
using EShop.Payment.Application.Payments.Common;

namespace EShop.Payment.API.Infrastructure.Export;

/// <summary>
/// Renders the accounting export (Admin panel S10, endpoint #69). It lives in the API layer because where a comma
/// goes in a file is a presentation decision; the query that produces the rows knows nothing about CSV.
/// </summary>
public static class PaymentCsvWriter
{
    /// <summary>The header row, and by construction the field order of every data row.</summary>
    public static readonly string[] Columns =
    [
        "Id",
        "OrderId",
        "UserId",
        "Amount",
        "Currency",
        "PaymentMethod",
        "Status",
        "PaymentIntentId",
        "ErrorMessage",
        "CreatedAt",
        "ProcessedAt"
    ];

    public static string Write(IEnumerable<PaymentDto> payments)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(',', Columns)).Append("\r\n");

        foreach (var payment in payments)
        {
            builder
                .Append(Field(payment.Id.ToString())).Append(',')
                .Append(Field(payment.OrderId.ToString())).Append(',')
                .Append(Field(payment.UserId)).Append(',')
                // Invariant culture, always: a machine writing "1234,56" for an amount on a comma-separated line
                // produces a file that parses into the wrong number of columns on a German-locale server.
                .Append(Field(payment.Amount.ToString("0.00", CultureInfo.InvariantCulture))).Append(',')
                .Append(Field(payment.Currency)).Append(',')
                .Append(Field(payment.PaymentMethod)).Append(',')
                .Append(Field(payment.Status)).Append(',')
                .Append(Field(payment.PaymentIntentId)).Append(',')
                .Append(Field(payment.ErrorMessage)).Append(',')
                .Append(Field(Timestamp(payment.CreatedAt))).Append(',')
                .Append(Field(Timestamp(payment.ProcessedAt)))
                .Append("\r\n");
        }

        return builder.ToString();
    }

    /// <summary>Round-trip UTC, so a spreadsheet import cannot reinterpret an instant in its own time zone.</summary>
    private static string Timestamp(DateTime? value)
        => value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>
    /// One field, quoted per RFC 4180 and made inert for a spreadsheet.
    ///
    /// <para><b>Both halves matter and neither is cosmetic.</b> Quoting is needed because these fields carry real
    /// free text: <c>ErrorMessage</c> holds Stripe's decline reasons and an operator's own refund note, and a comma or
    /// a quote in one would otherwise shift every later column of that row silently. The leading apostrophe is CSV
    /// injection defence: a field opening with <c>=</c>, <c>+</c>, <c>-</c>, <c>@</c>, tab or carriage return is
    /// treated by Excel and Sheets as a <i>formula</i>, so a refund reason typed as <c>=cmd|…</c> becomes code the
    /// accountant's spreadsheet offers to run. Quoting alone does not stop that — Excel evaluates the contents of a
    /// quoted field too.</para>
    /// </summary>
    private static string Field(string? value)
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
}
