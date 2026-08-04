using Microsoft.EntityFrameworkCore.Migrations;
using VSHelpDesk.Infrastructure.Persistence;

#nullable disable

namespace VSHelpDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketNumberSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
                return;

            migrationBuilder.Sql(
                $"""
                CREATE SEQUENCE IF NOT EXISTS {TicketNumberGenerator.SequenceName}
                    AS bigint
                    START WITH 1
                    INCREMENT BY 1
                    MINVALUE 1
                    NO MAXVALUE
                    CACHE 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider != "Npgsql.EntityFrameworkCore.PostgreSQL")
                return;

            migrationBuilder.Sql($"DROP SEQUENCE IF EXISTS {TicketNumberGenerator.SequenceName};");
        }
    }
}
