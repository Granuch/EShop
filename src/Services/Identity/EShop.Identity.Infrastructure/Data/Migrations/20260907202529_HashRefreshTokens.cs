using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EShop.Identity.Infrastructure.Data.Migrations
{
    /// <summary>
    /// SEC-04: stores refresh tokens as SHA-256 hashes instead of plaintext.
    ///
    /// <para>
    /// <b>This migration signs every user out.</b> The plaintext token is the only preimage of
    /// the hash we now need, and it is exactly what is being removed, so there is nothing to
    /// backfill from — the existing rows cannot be converted and are deleted. Every client holding
    /// a refresh token must log in again. There is no way to avoid this short of a dual-write
    /// window, which would mean keeping the plaintext column for longer, i.e. keeping the
    /// vulnerability for longer.
    /// </para>
    ///
    /// <para>
    /// The delete is also what makes the migration <i>apply</i>: EF scaffolds the new
    /// <c>TokenHash</c> as NOT NULL with a constant default and then puts a unique index on it,
    /// which fails with a duplicate-key error the moment the table holds more than one row.
    /// </para>
    /// </summary>
    public partial class HashRefreshTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing tokens cannot be migrated — see the class summary. This must run before
            // the unique index on TokenHash is created.
            migrationBuilder.Sql(@"DELETE FROM refresh_tokens;");

            migrationBuilder.DropIndex(
                name: "IX_refresh_tokens_Token",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "ReplacedByToken",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "Token",
                table: "refresh_tokens");

            migrationBuilder.AddColumn<string>(
                name: "ReplacedByTokenHash",
                table: "refresh_tokens",
                type: "char(64)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TokenHash",
                table: "refresh_tokens",
                type: "char(64)",
                nullable: false,
                defaultValue: "");

            // EF scaffolds a constant default to satisfy NOT NULL on the existing rows. Those rows
            // are gone, and leaving the default behind means an INSERT that omitted TokenHash
            // would silently store 64 spaces instead of failing — and would even satisfy the
            // unique index, once.
            migrationBuilder.Sql(@"ALTER TABLE refresh_tokens ALTER COLUMN ""TokenHash"" DROP DEFAULT;");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_TokenHash",
                table: "refresh_tokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_refresh_tokens_TokenHash",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "ReplacedByTokenHash",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "TokenHash",
                table: "refresh_tokens");

            migrationBuilder.AddColumn<string>(
                name: "ReplacedByToken",
                table: "refresh_tokens",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Token",
                table: "refresh_tokens",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_Token",
                table: "refresh_tokens",
                column: "Token",
                unique: true);
        }
    }
}
