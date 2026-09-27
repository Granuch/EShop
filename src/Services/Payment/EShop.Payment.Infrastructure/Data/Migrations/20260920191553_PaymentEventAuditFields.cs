using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EShop.Payment.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class PaymentEventAuditFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Scaffolded as `defaultValue: DateTime.MinValue`, which Npgsql renders as
            // `NOT NULL DEFAULT TIMESTAMPTZ '-infinity'`. That is wrong twice over on a populated table, so the two
            // statements after it are hand-written:
            //   * every existing row would be backfilled with -infinity, when OccurredAt is a truthful
            //     approximation — CreatedAt is the save time, and every row written so far was saved in the same
            //     transaction as the transition it records;
            //   * the DEFAULT would then stay on the column forever, so a raw INSERT that forgot CreatedAt would
            //     silently store -infinity instead of failing. SetAuditFields always supplies a value, so nothing
            //     needs the default.
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "PaymentEvents",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.Sql("""UPDATE "PaymentEvents" SET "CreatedAt" = "OccurredAt";""");

            migrationBuilder.Sql("""ALTER TABLE "PaymentEvents" ALTER COLUMN "CreatedAt" DROP DEFAULT;""");

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "PaymentEvents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "PaymentEvents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "PaymentEvents",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "PaymentEvents");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "PaymentEvents");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "PaymentEvents");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "PaymentEvents");
        }
    }
}
