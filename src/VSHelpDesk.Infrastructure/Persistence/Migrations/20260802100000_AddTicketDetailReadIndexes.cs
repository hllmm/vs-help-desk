using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VSHelpDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTicketDetailReadIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TicketMessages_TicketId_CreatedAt",
                table: "TicketMessages");

            migrationBuilder.DropIndex(
                name: "IX_TicketAttachments_TicketMessageId",
                table: "TicketAttachments");

            migrationBuilder.CreateIndex(
                name: "IX_TicketMessages_TicketId_CreatedAt_Id",
                table: "TicketMessages",
                columns: new[] { "TicketId", "CreatedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_TicketAttachments_TicketMessageId_CreatedAt_Id",
                table: "TicketAttachments",
                columns: new[] { "TicketMessageId", "CreatedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TicketMessages_TicketId_CreatedAt_Id",
                table: "TicketMessages");

            migrationBuilder.DropIndex(
                name: "IX_TicketAttachments_TicketMessageId_CreatedAt_Id",
                table: "TicketAttachments");

            migrationBuilder.CreateIndex(
                name: "IX_TicketMessages_TicketId_CreatedAt",
                table: "TicketMessages",
                columns: new[] { "TicketId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TicketAttachments_TicketMessageId",
                table: "TicketAttachments",
                column: "TicketMessageId");
        }
    }
}
