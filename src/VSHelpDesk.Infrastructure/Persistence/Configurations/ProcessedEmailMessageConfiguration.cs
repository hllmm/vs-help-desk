using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Persistence.Configurations;

public sealed class ProcessedEmailMessageConfiguration : IEntityTypeConfiguration<ProcessedEmailMessage>
{
    public const string IdempotencyUniqueIndexName =
        "UX_ProcessedEmailMessages_IdempotencyKey";

    public void Configure(EntityTypeBuilder<ProcessedEmailMessage> builder)
    {
        builder.ToTable("ProcessedEmailMessages");
        builder.HasKey(message => message.Id);

        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.Property(message => message.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(998);
        builder.Property(message => message.SourceMessageId).HasMaxLength(998);
        builder.Property(message => message.ProcessingNote).HasMaxLength(500);
        builder.Property(message => message.AcknowledgementLastError).HasMaxLength(500);
        builder.Property(message => message.Disposition).IsRequired().HasConversion<int>();
        builder.Property(message => message.AcknowledgementStatus).IsRequired().HasConversion<int>();
        builder.Property(message => message.AcknowledgementAttempts).IsRequired();
        builder.Property(message => message.ProcessedAt).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(message => message.AcknowledgementLastAttemptAt)
            .HasColumnType("timestamp with time zone");
        builder.Property(message => message.AcknowledgementNextAttemptAt)
            .HasColumnType("timestamp with time zone");
        builder.Property(message => message.AcknowledgementSentAt)
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(message => message.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName(IdempotencyUniqueIndexName);
        builder.HasIndex(
            message => new
            {
                message.AcknowledgementStatus,
                message.AcknowledgementNextAttemptAt
            })
            .HasDatabaseName("IX_ProcessedEmailMessages_AcknowledgementStatus_Acknowledgemen~");

        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(message => message.TicketId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
