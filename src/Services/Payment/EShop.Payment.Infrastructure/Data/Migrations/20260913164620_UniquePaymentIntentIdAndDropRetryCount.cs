using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EShop.Payment.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Payment audit Stage 10.
    /// <list type="bullet">
    ///   <item>M11: <c>PaymentIntentId</c> becomes unique among payments that have an intent. A database where two
    ///   payments already share one is refused before anything changes, with a message naming the problem. The bare
    ///   unique violation would name only the index.</item>
    ///   <item>D12: <c>RetryCount</c> is dropped. It was mapped but never written, and every row held 0.</item>
    /// </list>
    /// </summary>
    public partial class UniquePaymentIntentIdAndDropRetryCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE shared integer;
                BEGIN
                    SELECT count(*) INTO shared FROM (
                        SELECT "PaymentIntentId" FROM "PaymentTransactions"
                        WHERE "PaymentIntentId" <> ''
                        GROUP BY "PaymentIntentId"
                        HAVING count(*) > 1) duplicated;
                    IF shared > 0 THEN
                        RAISE EXCEPTION 'Cannot make PaymentIntentId unique: % Stripe intent id(s) are recorded on more than one payment. Resolve them before applying this migration.', shared;
                    END IF;
                END $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_PaymentTransactions_PaymentIntentId",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "RetryCount",
                table: "PaymentTransactions");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_PaymentIntentId",
                table: "PaymentTransactions",
                column: "PaymentIntentId",
                unique: true,
                filter: "\"PaymentIntentId\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentTransactions_PaymentIntentId",
                table: "PaymentTransactions");

            migrationBuilder.AddColumn<int>(
                name: "RetryCount",
                table: "PaymentTransactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_PaymentIntentId",
                table: "PaymentTransactions",
                column: "PaymentIntentId");
        }
    }
}
