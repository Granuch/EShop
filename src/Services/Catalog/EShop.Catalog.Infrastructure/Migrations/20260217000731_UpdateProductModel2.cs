using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EShop.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateProductModel2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_Sku_Trgm",
                table: "Products");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Sku_Trgm",
                table: "Products",
                column: "Sku")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_Sku_Trgm",
                table: "Products");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Sku_Trgm",
                table: "Products",
                column: "Sku",
                unique: true)
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }
    }
}
