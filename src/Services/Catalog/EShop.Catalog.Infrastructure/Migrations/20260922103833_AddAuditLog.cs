using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EShop.Catalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Service = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ActorUserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ActorName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ErrorCode = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PayloadJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_ActorUserId",
                table: "audit_log",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_EntityType_EntityId",
                table: "audit_log",
                columns: new[] { "EntityType", "EntityId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_log");
        }
    }
}
