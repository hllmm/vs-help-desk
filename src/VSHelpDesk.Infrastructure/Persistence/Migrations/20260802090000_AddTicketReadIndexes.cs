using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VSHelpDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketReadIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_CustomerEmail_Trgm",
                table: "Tickets",
                column: "CustomerEmail")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_CustomerName_Trgm",
                table: "Tickets",
                column: "CustomerName")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_LastActivityAt_TicketNumber",
                table: "Tickets",
                columns: new[] { "LastActivityAt", "TicketNumber" },
                descending: new[] { true, false });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_Status_LastActivityAt_TicketNumber",
                table: "Tickets",
                columns: new[] { "Status", "LastActivityAt", "TicketNumber" },
                descending: new[] { false, true, false });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_Status_WaitingCustomerSince_Id",
                table: "Tickets",
                columns: new[] { "Status", "WaitingCustomerSince", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_Subject_Trgm",
                table: "Tickets",
                column: "Subject")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_TicketNumber_Trgm",
                table: "Tickets",
                column: "TicketNumber")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tickets_CustomerEmail_Trgm",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_CustomerName_Trgm",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_LastActivityAt_TicketNumber",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_Status_LastActivityAt_TicketNumber",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_Status_WaitingCustomerSince_Id",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_Subject_Trgm",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_TicketNumber_Trgm",
                table: "Tickets");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
