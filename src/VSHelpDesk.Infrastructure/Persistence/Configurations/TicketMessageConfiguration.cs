using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Persistence.Configurations;

public sealed class TicketMessageConfiguration : IEntityTypeConfiguration<TicketMessage>
{
    public void Configure(EntityTypeBuilder<TicketMessage> builder)
    {
        builder.ToTable("TicketMessages");
        builder.HasKey(message => message.Id);

        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.Property(message => message.TicketId).IsRequired();
        builder.Property(message => message.SenderType).IsRequired().HasConversion<int>();
        builder.Property(message => message.Content).IsRequired();
        builder.Property(message => message.IsHtml).IsRequired();
        builder.Property(message => message.CreatedAt).IsRequired().HasColumnType("timestamp with time zone");

        builder.HasIndex(message => new { message.TicketId, message.CreatedAt, message.Id })
            .HasDatabaseName("IX_TicketMessages_TicketId_CreatedAt_Id")
            .IsDescending(false, true, true);

        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(message => message.TicketId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(message => message.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
