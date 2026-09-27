using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EShop.Payment.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Admin panel S10 (M9). The admin payment list and its export order by <c>CreatedAt DESC, Id DESC</c>.
    /// <para>The plan's M9 asks for indexes on <c>(Status, CreatedAt)</c>, <c>(UserId, CreatedAt)</c> and
    /// <c>(OrderId)</c>. <b>All three already existed</b>, from <c>InitialCreate</c>. What was genuinely missing is
    /// the direction and the tie-break: Postgres can read a whole index backwards, but not one column of a composite,
    /// so an all-ascending <c>(Status, CreatedAt)</c> cannot serve <c>WHERE Status = … ORDER BY CreatedAt DESC, Id
    /// DESC</c>. And the unfiltered list has no leading predicate at all, while <c>PaymentTransactions</c> had no
    /// index on <c>CreatedAt</c> alone.</para>
    /// <para><c>IX_PaymentTransactions_Status_CreatedAt</c> is dropped rather than kept beside the new composite,
    /// which covers its lookups as a leading-column prefix — the same trade Ordering's M6 made.</para>
    /// </summary>
    public partial class AdminPaymentListIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentTransactions_Status_CreatedAt",
                table: "PaymentTransactions");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_CreatedAt_Id",
                table: "PaymentTransactions",
                columns: new[] { "CreatedAt", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_Status_CreatedAt_Id",
                table: "PaymentTransactions",
                columns: new[] { "Status", "CreatedAt", "Id" },
                descending: new[] { false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentTransactions_CreatedAt_Id",
                table: "PaymentTransactions");

            migrationBuilder.DropIndex(
                name: "IX_PaymentTransactions_Status_CreatedAt_Id",
                table: "PaymentTransactions");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_Status_CreatedAt",
                table: "PaymentTransactions",
                columns: new[] { "Status", "CreatedAt" });
        }
    }
}
