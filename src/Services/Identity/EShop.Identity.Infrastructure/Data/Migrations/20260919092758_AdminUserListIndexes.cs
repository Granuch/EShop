using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EShop.Identity.Infrastructure.Data.Migrations
{
    /// <summary>
    /// M10 (Admin panel S6). Two indexes behind the admin user list and its stats tile.
    /// </summary>
    /// <remarks>
    /// <c>CreatedAt</c> carries both the list's default sort (newest first) and the tile's
    /// "new in period" count. <c>(IsDeleted, IsActive)</c> leads with <c>IsDeleted</c> because the
    /// <c>!u.IsDeleted</c> global query filter puts a predicate on that column in every query —
    /// it is the one column always in the WHERE clause, the same reasoning behind Catalog's
    /// M2/M3 leading with <c>Status</c>. Purely additive: no column changes, and `migrations add`
    /// printed no "loss of data" warning.
    /// <para>
    /// Note Identity keeps its migrations in <c>Infrastructure/Data/Migrations</c>, not in
    /// <c>Infrastructure/Migrations</c> where Catalog's live — so a glob written for one service
    /// finds nothing in the other.
    /// </para>
    /// </remarks>
    public partial class AdminUserListIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_users_CreatedAt",
                table: "users",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_users_IsDeleted_IsActive",
                table: "users",
                columns: new[] { "IsDeleted", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_CreatedAt",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_IsDeleted_IsActive",
                table: "users");
        }
    }
}
