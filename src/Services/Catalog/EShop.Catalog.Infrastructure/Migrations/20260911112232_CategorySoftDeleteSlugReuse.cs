using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EShop.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CategorySoftDeleteSlugReuse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Categories_ParentCategoryId_Slug",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Categories_Slug",
                table: "Categories");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ParentCategoryId_Slug",
                table: "Categories",
                columns: new[] { "ParentCategoryId", "Slug" },
                unique: true,
                filter: "\"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Slug",
                table: "Categories",
                column: "Slug",
                unique: true,
                filter: "\"ParentCategoryId\" IS NULL AND \"IsActive\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Categories_ParentCategoryId_Slug",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Categories_Slug",
                table: "Categories");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ParentCategoryId_Slug",
                table: "Categories",
                columns: new[] { "ParentCategoryId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Slug",
                table: "Categories",
                column: "Slug",
                unique: true,
                filter: "\"ParentCategoryId\" IS NULL");
        }
    }
}
