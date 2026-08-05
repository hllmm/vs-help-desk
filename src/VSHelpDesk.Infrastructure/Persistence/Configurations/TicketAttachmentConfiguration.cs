using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Persistence.Configurations;

public sealed class TicketAttachmentConfiguration : IEntityTypeConfiguration<TicketAttachment>
{
    public void Configure(EntityTypeBuilder<TicketAttachment> builder)
    {
        builder.ToTable("TicketAttachments");
        builder.HasKey(attachment => attachment.Id);

        builder.Property(attachment => attachment.Id).ValueGeneratedNever();
        builder.Property(attachment => attachment.TicketMessageId).IsRequired();
        builder.Property(attachment => attachment.FileName).IsRequired().HasMaxLength(255);
        builder.Property(attachment => attachment.StoredFileName).IsRequired().HasMaxLength(260);
        builder.Property(attachment => attachment.FilePath).IsRequired().HasMaxLength(1024);
        builder.Property(attachment => attachment.ContentType).IsRequired().HasMaxLength(200);
        builder.Property(attachment => attachment.FileSize).IsRequired();
        builder.Property(attachment => attachment.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(attachment => new
        {
            attachment.TicketMessageId,
            attachment.CreatedAt,
            attachment.Id
        })
            .HasDatabaseName("IX_TicketAttachments_TicketMessageId_CreatedAt_Id");
        builder.HasIndex(attachment => attachment.StoredFileName).IsUnique();

        builder.HasOne<TicketMessage>()
            .WithMany()
            .HasForeignKey(attachment => attachment.TicketMessageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
