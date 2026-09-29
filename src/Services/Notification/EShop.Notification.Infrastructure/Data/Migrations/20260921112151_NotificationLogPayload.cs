using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EShop.Notification.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class NotificationLogPayload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Payload",
                table: "NotificationLogs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Payload",
                table: "NotificationLogs");
        }
    }
}
