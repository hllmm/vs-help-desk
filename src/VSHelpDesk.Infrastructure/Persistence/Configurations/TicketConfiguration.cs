using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Persistence.Configurations;

public sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        builder.ToTable("Tickets");
        builder.HasKey(ticket => ticket.Id);

        builder.Property(ticket => ticket.Id).ValueGeneratedNever();
        builder.Property(ticket => ticket.TicketNumber).IsRequired().HasMaxLength(32);
        builder.Property(ticket => ticket.ReplyToken).IsRequired().HasMaxLength(32);
        builder.Property(ticket => ticket.Subject).IsRequired().HasMaxLength(500);
        builder.Property(ticket => ticket.CustomerName).IsRequired().HasMaxLength(200);
        builder.Property(ticket => ticket.CustomerEmail).IsRequired().HasMaxLength(255);
        builder.Property(ticket => ticket.Status).IsRequired().HasConversion<int>();
        builder.Property(ticket => ticket.WaitingCustomerSince).HasColumnType("timestamp with time zone");
        builder.Property(ticket => ticket.CreatedAt).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(ticket => ticket.UpdatedAt).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(ticket => ticket.ResolvedAt).HasColumnType("timestamp with time zone");
        builder.Property(ticket => ticket.LastActivityAt).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(ticket => ticket.Version).IsRowVersion();

        builder.HasIndex(ticket => ticket.TicketNumber).IsUnique();
        builder.HasIndex(ticket => ticket.ReplyToken).IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(ticket => ticket.AssignedUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(ticket => ticket.ClosedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
