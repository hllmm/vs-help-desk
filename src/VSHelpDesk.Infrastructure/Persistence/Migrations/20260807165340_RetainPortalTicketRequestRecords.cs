using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VSHelpDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetainPortalTicketRequestRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PortalTicketRequests_Tickets_TicketId",
                table: "PortalTicketRequests");

            migrationBuilder.AddForeignKey(
                name: "FK_PortalTicketRequests_Tickets_TicketId",
                table: "PortalTicketRequests",
                column: "TicketId",
                principalTable: "Tickets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PortalTicketRequests_Tickets_TicketId",
                table: "PortalTicketRequests");

            migrationBuilder.AddForeignKey(
                name: "FK_PortalTicketRequests_Tickets_TicketId",
                table: "PortalTicketRequests",
                column: "TicketId",
                principalTable: "Tickets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
