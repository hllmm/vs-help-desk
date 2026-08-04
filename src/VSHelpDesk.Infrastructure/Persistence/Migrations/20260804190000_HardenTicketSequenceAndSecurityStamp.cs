using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VSHelpDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenTicketSequenceAndSecurityStamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                table: "Users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            if (migrationBuilder.ActiveProvider?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
            {
                migrationBuilder.Sql(@"
                    UPDATE ""Users""
                    SET ""SecurityStamp"" = md5(random()::text || clock_timestamp()::text)
                    WHERE ""SecurityStamp"" IS NULL OR ""SecurityStamp"" = '';
                ");
                migrationBuilder.Sql("ALTER SEQUENCE ticket_number_seq MAXVALUE 999999 NO CYCLE;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                table: "Users");

            if (migrationBuilder.ActiveProvider?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true)
            {
                migrationBuilder.Sql("ALTER SEQUENCE ticket_number_seq NO MAXVALUE NO CYCLE;");
            }
        }
    }
}
