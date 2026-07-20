using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Persistence.Configurations;

public sealed class ProcessedEmailMessageConfiguration : IEntityTypeConfiguration<ProcessedEmailMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedEmailMessage> builder)
    {
        builder.ToTable("ProcessedEmailMessages");
        builder.HasKey(message => message.Id);

        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.Property(message => message.MessageId).IsRequired().HasMaxLength(998);
        builder.Property(message => message.ProcessedAt).IsRequired().HasColumnType("timestamp with time zone");

        builder.HasIndex(message => message.MessageId).IsUnique();

        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(message => message.TicketId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
