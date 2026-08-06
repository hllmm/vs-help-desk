using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VSHelpDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserAuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BeforeRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    AfterRole = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    BeforeIsActive = table.Column<bool>(type: "boolean", nullable: true),
                    AfterIsActive = table.Column<bool>(type: "boolean", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAuditEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserAuditEvents_ActorUserId",
                table: "UserAuditEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAuditEvents_TargetUserId_CreatedAt",
                table: "UserAuditEvents",
                columns: new[] { "TargetUserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserAuditEvents");
        }
    }
}
