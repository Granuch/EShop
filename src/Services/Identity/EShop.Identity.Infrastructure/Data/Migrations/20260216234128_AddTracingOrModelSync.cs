using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EShop.Identity.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTracingOrModelSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_OutboxMessages",
                table: "OutboxMessages");

            migrationBuilder.RenameTable(
                name: "OutboxMessages",
                newName: "outbox_messages");

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "outbox_messages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<int>(
                name: "RetryCount",
                table: "outbox_messages",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "LastError",
                table: "outbox_messages",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CorrelationId",
                table: "outbox_messages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AggregateType",
                table: "outbox_messages",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AggregateId",
                table: "outbox_messages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "outbox_messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_outbox_messages",
                table: "outbox_messages",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "processed_messages",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageType = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ProcessedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_messages", x => x.MessageId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_CorrelationId",
                table: "outbox_messages",
                column: "CorrelationId",
                filter: "\"CorrelationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedOnUtc_OccurredOnUtc",
                table: "outbox_messages",
                columns: new[] { "ProcessedOnUtc", "OccurredOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedOnUtc_RetryCount",
                table: "outbox_messages",
                columns: new[] { "ProcessedOnUtc", "RetryCount" },
                filter: "\"ProcessedOnUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_Status_OccurredOnUtc",
                table: "outbox_messages",
                columns: new[] { "Status", "OccurredOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedMessages_ProcessedOnUtc",
                table: "processed_messages",
                column: "ProcessedOnUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "processed_messages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_outbox_messages",
                table: "outbox_messages");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_CorrelationId",
                table: "outbox_messages");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_ProcessedOnUtc_OccurredOnUtc",
                table: "outbox_messages");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_ProcessedOnUtc_RetryCount",
                table: "outbox_messages");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_Status_OccurredOnUtc",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "outbox_messages");

            migrationBuilder.RenameTable(
                name: "outbox_messages",
                newName: "OutboxMessages");

            migrationBuilder.AlterColumn<string>(
                name: "Type",
                table: "OutboxMessages",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(500)",
                oldMaxLength: 500);

            migrationBuilder.AlterColumn<int>(
                name: "RetryCount",
                table: "OutboxMessages",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "LastError",
                table: "OutboxMessages",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CorrelationId",
                table: "OutboxMessages",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AggregateType",
                table: "OutboxMessages",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(250)",
                oldMaxLength: 250,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AggregateId",
                table: "OutboxMessages",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_OutboxMessages",
                table: "OutboxMessages",
                column: "Id");
        }
    }
}
