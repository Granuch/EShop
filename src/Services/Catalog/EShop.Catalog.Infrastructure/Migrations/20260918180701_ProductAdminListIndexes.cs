using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EShop.Catalog.Infrastructure.Migrations
{
    /// <summary>
    /// M2/M3 (Admin panel S4). Two composite indexes supporting the admin list's new
    /// <c>?StockBelow=</c> and <c>?Status=</c> filters and the low-stock dashboard read.
    /// </summary>
    /// <remarks>
    /// Both lead with <c>Status</c> rather than being single-column indexes on <c>StockQuantity</c>
    /// and <c>Status</c>, because <c>ProductQueryService.ApplyFilter</c> applies a Status predicate
    /// to every non-admin read — so it is the one column always in the WHERE clause, and leading
    /// with it is what lets Postgres range-scan instead of combining two access paths. Purely
    /// additive: no column changes, nothing destructive, and `migrations add` printed no
    /// "loss of data" warning.
    /// </remarks>
    public partial class ProductAdminListIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Products_Status_CategoryId",
                table: "Products",
                columns: new[] { "Status", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Status_StockQuantity",
                table: "Products",
                columns: new[] { "Status", "StockQuantity" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_Status_CategoryId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_Status_StockQuantity",
                table: "Products");
        }
    }
}
