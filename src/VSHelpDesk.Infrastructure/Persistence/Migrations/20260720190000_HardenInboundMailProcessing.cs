using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VSHelpDesk.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HardenInboundMailProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MessageId",
                table: "ProcessedEmailMessages",
                newName: "IdempotencyKey");

            migrationBuilder.RenameIndex(
                name: "IX_ProcessedEmailMessages_MessageId",
                table: "ProcessedEmailMessages",
                newName: "UX_ProcessedEmailMessages_IdempotencyKey");

            migrationBuilder.AddColumn<string>(
                name: "SourceMessageId",
                table: "ProcessedEmailMessages",
                type: "character varying(998)",
                maxLength: 998,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessingNote",
                table: "ProcessedEmailMessages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Disposition",
                table: "ProcessedEmailMessages",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "AcknowledgementStatus",
                table: "ProcessedEmailMessages",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "AcknowledgementAttempts",
                table: "ProcessedEmailMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "AcknowledgementLastAttemptAt",
                table: "ProcessedEmailMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AcknowledgementNextAttemptAt",
                table: "ProcessedEmailMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AcknowledgementSentAt",
                table: "ProcessedEmailMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcknowledgementLastError",
                table: "ProcessedEmailMessages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "ProcessedEmailMessages"
                SET "SourceMessageId" = "IdempotencyKey",
                    "Disposition" = 1,
                    "AcknowledgementStatus" = 1,
                    "AcknowledgementAttempts" = 0,
                    "AcknowledgementLastAttemptAt" = NULL,
                    "AcknowledgementNextAttemptAt" = NULL,
                    "AcknowledgementSentAt" = NULL,
                    "AcknowledgementLastError" = NULL;
                """);

            migrationBuilder.Sql(
                """
                ALTER SEQUENCE ticket_number_seq
                MAXVALUE 999999
                NO CYCLE;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedEmailMessages_AcknowledgementStatus_Acknowledgemen~",
                table: "ProcessedEmailMessages",
                columns: new[] { "AcknowledgementStatus", "AcknowledgementNextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProcessedEmailMessages_AcknowledgementStatus_Acknowledgemen~",
                table: "ProcessedEmailMessages");

            migrationBuilder.DropColumn(
                name: "AcknowledgementAttempts",
                table: "ProcessedEmailMessages");

            migrationBuilder.DropColumn(
                name: "AcknowledgementLastAttemptAt",
                table: "ProcessedEmailMessages");

            migrationBuilder.DropColumn(
                name: "AcknowledgementLastError",
                table: "ProcessedEmailMessages");

            migrationBuilder.DropColumn(
                name: "AcknowledgementNextAttemptAt",
                table: "ProcessedEmailMessages");

            migrationBuilder.DropColumn(
                name: "AcknowledgementSentAt",
                table: "ProcessedEmailMessages");

            migrationBuilder.DropColumn(
                name: "AcknowledgementStatus",
                table: "ProcessedEmailMessages");

            migrationBuilder.DropColumn(
                name: "Disposition",
                table: "ProcessedEmailMessages");

            migrationBuilder.DropColumn(
                name: "ProcessingNote",
                table: "ProcessedEmailMessages");

            migrationBuilder.DropColumn(
                name: "SourceMessageId",
                table: "ProcessedEmailMessages");

            migrationBuilder.RenameColumn(
                name: "IdempotencyKey",
                table: "ProcessedEmailMessages",
                newName: "MessageId");

            migrationBuilder.RenameIndex(
                name: "UX_ProcessedEmailMessages_IdempotencyKey",
                table: "ProcessedEmailMessages",
                newName: "IX_ProcessedEmailMessages_MessageId");

            migrationBuilder.Sql(
                """
                ALTER SEQUENCE ticket_number_seq NO MAXVALUE NO CYCLE;
                """);
        }
    }
}
