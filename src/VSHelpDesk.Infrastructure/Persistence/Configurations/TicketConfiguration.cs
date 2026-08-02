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

        builder.HasIndex(ticket => ticket.TicketNumber, "IX_Tickets_TicketNumber")
            .IsUnique();
        builder.HasIndex(ticket => new { ticket.LastActivityAt, ticket.TicketNumber })
            .HasDatabaseName("IX_Tickets_LastActivityAt_TicketNumber")
            .IsDescending(true, false);
        builder.HasIndex(ticket => new { ticket.Status, ticket.LastActivityAt, ticket.TicketNumber })
            .HasDatabaseName("IX_Tickets_Status_LastActivityAt_TicketNumber")
            .IsDescending(false, true, false);
        builder.HasIndex(ticket => new { ticket.Status, ticket.WaitingCustomerSince, ticket.Id })
            .HasDatabaseName("IX_Tickets_Status_WaitingCustomerSince_Id")
            .IsDescending(false, false, false);

        ConfigureTrigramIndex(
            builder.HasIndex(ticket => ticket.TicketNumber, "IX_Tickets_TicketNumber_Trgm"),
            "IX_Tickets_TicketNumber_Trgm");
        ConfigureTrigramIndex(
            builder.HasIndex(ticket => ticket.Subject),
            "IX_Tickets_Subject_Trgm");
        ConfigureTrigramIndex(
            builder.HasIndex(ticket => ticket.CustomerName),
            "IX_Tickets_CustomerName_Trgm");
        ConfigureTrigramIndex(
            builder.HasIndex(ticket => ticket.CustomerEmail),
            "IX_Tickets_CustomerEmail_Trgm");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(ticket => ticket.AssignedUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(ticket => ticket.ClosedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    private static void ConfigureTrigramIndex(
        IndexBuilder<Ticket> index,
        string databaseName)
    {
        index
            .HasDatabaseName(databaseName)
            .HasMethod("gin")
            .HasOperators("gin_trgm_ops");
    }
}
