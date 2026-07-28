using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VSHelpDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurityHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SecurityVersion",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "ReplyToken",
                table: "Tickets",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "Tickets"
                SET "ReplyToken" = lower(replace(gen_random_uuid()::text, '-', ''))
                WHERE "ReplyToken" IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "ReplyToken",
                table: "Tickets",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "UserAdministrationAuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BeforeValue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AfterValue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAdministrationAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_ReplyToken",
                table: "Tickets",
                column: "ReplyToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAdministrationAuditLogs_TargetUserId_OccurredAt",
                table: "UserAdministrationAuditLogs",
                columns: new[] { "TargetUserId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserAdministrationAuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_ReplyToken",
                table: "Tickets");

            migrationBuilder.DropColumn(
                name: "SecurityVersion",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ReplyToken",
                table: "Tickets");
        }
    }
}
