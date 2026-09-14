using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EShop.Notification.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class NotificationDeliveryAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "RecipientEmail",
                table: "NotificationLogs",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(320)",
                oldMaxLength: 320);

            migrationBuilder.AddColumn<DateTime>(
                name: "AttemptStartedAt",
                table: "NotificationLogs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "NotificationLogs",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttemptStartedAt",
                table: "NotificationLogs");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "NotificationLogs");

            // Rows written since this migration have no recipient until it is resolved, and SET NOT NULL would refuse
            // them. They take the empty string the column default below gives new rows.
            migrationBuilder.Sql("UPDATE \"NotificationLogs\" SET \"RecipientEmail\" = '' WHERE \"RecipientEmail\" IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "RecipientEmail",
                table: "NotificationLogs",
                type: "character varying(320)",
                maxLength: 320,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(320)",
                oldMaxLength: 320,
                oldNullable: true);
        }
    }
}
