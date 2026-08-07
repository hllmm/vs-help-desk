using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Persistence.Configurations;

public sealed class PortalTicketRequestConfiguration : IEntityTypeConfiguration<PortalTicketRequest>
{
    public const string UserKeyUniqueIndexName =
        "UX_PortalTicketRequests_UserId_IdempotencyKey";

    public void Configure(EntityTypeBuilder<PortalTicketRequest> builder)
    {
        builder.ToTable("PortalTicketRequests");
        builder.HasKey(request => request.Id);

        builder.Property(request => request.Id).ValueGeneratedNever();
        builder.Property(request => request.IdempotencyKey)
            .IsRequired()
            .HasMaxLength(36);
        builder.Property(request => request.RequestHash)
            .IsRequired()
            .HasMaxLength(64);
        builder.Property(request => request.CreatedAtUtc)
            .IsRequired()
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(
                request => new
                {
                    request.UserId,
                    request.IdempotencyKey
                })
            .IsUnique()
            .HasDatabaseName(UserKeyUniqueIndexName);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(request => request.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(request => request.TicketId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
