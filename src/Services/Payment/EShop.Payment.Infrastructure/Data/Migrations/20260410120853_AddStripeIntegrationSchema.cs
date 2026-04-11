using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EShop.Payment.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeIntegrationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StripeCustomerId",
                table: "PaymentTransactions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeStatus",
                table: "PaymentTransactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PaymentCustomers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    StripeCustomerId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentCustomers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedStripeWebhookEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EventType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProcessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedStripeWebhookEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_PaymentIntentId",
                table: "PaymentTransactions",
                column: "PaymentIntentId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCustomers_StripeCustomerId",
                table: "PaymentCustomers",
                column: "StripeCustomerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentCustomers_UserId",
                table: "PaymentCustomers",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedStripeWebhookEvents_EventId",
                table: "ProcessedStripeWebhookEvents",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedStripeWebhookEvents_ProcessedAt",
                table: "ProcessedStripeWebhookEvents",
                column: "ProcessedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentCustomers");

            migrationBuilder.DropTable(
                name: "ProcessedStripeWebhookEvents");

            migrationBuilder.DropIndex(
                name: "IX_PaymentTransactions_PaymentIntentId",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "StripeCustomerId",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "StripeStatus",
                table: "PaymentTransactions");
        }
    }
}
