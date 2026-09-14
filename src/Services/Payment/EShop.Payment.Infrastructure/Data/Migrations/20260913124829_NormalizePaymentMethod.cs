using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EShop.Payment.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Payment audit Stage 7 (H5). <c>PaymentMethod</c> is now an enum stored as its name ("Mock", "Stripe", "None").
    /// Before Stage 3 it was free text from the request, and reading any other string into the enum throws. So existing
    /// values are mapped first. Any case of "stripe" or "none" maps to Stripe or None, as the code already treated
    /// "stripe" case-insensitively. Anything else maps to Mock, because the simulator settled every such payment. No
    /// schema change: the column stays <c>varchar(50)</c>.
    /// </summary>
    public partial class NormalizePaymentMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "PaymentTransactions"
                SET "PaymentMethod" = CASE
                    WHEN lower("PaymentMethod") = 'stripe' THEN 'Stripe'
                    WHEN lower("PaymentMethod") = 'none' THEN 'None'
                    ELSE 'Mock'
                END
                WHERE "PaymentMethod" NOT IN ('Stripe', 'Mock', 'None');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The original free-text values are gone and cannot be restored. The normalized ones stay valid.
        }
    }
}
