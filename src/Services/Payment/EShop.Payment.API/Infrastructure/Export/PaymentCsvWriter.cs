using System.Text;
using EShop.BuildingBlocks.Infrastructure.Export;
using EShop.Payment.Application.Payments.Common;

namespace EShop.Payment.API.Infrastructure.Export;

/// <summary>
/// Renders the accounting export (Admin panel S10, endpoint #69). It lives in the API layer because where a comma
/// goes in a file is a presentation decision; the query that produces the rows knows nothing about CSV.
/// </summary>
/// <remarks>
/// The field rules — RFC 4180 quoting and the formula-injection apostrophe — moved to the shared
/// <see cref="SafeCsv"/> in admin panel S16, when Catalog gained a second export. This class now decides only the
/// columns; its output is unchanged, and <c>PaymentCsvWriterTests</c> still pins both rules end to end.
/// </remarks>
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
        builder.Append(string.Join(',', Columns)).Append(SafeCsv.LineEnd);

        foreach (var payment in payments)
        {
            builder
                .Append(SafeCsv.Field(payment.Id.ToString())).Append(',')
                .Append(SafeCsv.Field(payment.OrderId.ToString())).Append(',')
                .Append(SafeCsv.Field(payment.UserId)).Append(',')
                .Append(SafeCsv.Field(SafeCsv.Decimal(payment.Amount))).Append(',')
                .Append(SafeCsv.Field(payment.Currency)).Append(',')
                .Append(SafeCsv.Field(payment.PaymentMethod)).Append(',')
                .Append(SafeCsv.Field(payment.Status)).Append(',')
                .Append(SafeCsv.Field(payment.PaymentIntentId)).Append(',')
                .Append(SafeCsv.Field(payment.ErrorMessage)).Append(',')
                .Append(SafeCsv.Field(SafeCsv.Timestamp(payment.CreatedAt))).Append(',')
                .Append(SafeCsv.Field(SafeCsv.Timestamp(payment.ProcessedAt)))
                .Append(SafeCsv.LineEnd);
        }

        return builder.ToString();
    }
}
