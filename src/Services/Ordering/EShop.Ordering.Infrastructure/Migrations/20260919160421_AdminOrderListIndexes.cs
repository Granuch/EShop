using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EShop.Ordering.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AdminOrderListIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_Status",
                table: "Orders");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status_CreatedAt_Id",
                table: "Orders",
                columns: new[] { "Status", "CreatedAt", "Id" },
                descending: new[] { false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_Status_CreatedAt_Id",
                table: "Orders");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status",
                table: "Orders",
                column: "Status");
        }
    }
}
