using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Persistence.Configurations;

public sealed class UserAdministrationAuditLogConfiguration
    : IEntityTypeConfiguration<UserAdministrationAuditLog>
{
    public void Configure(
        EntityTypeBuilder<UserAdministrationAuditLog> builder)
    {
        builder.ToTable("UserAdministrationAuditLogs");
        builder.HasKey(log => log.Id);
        builder.Property(log => log.Id).ValueGeneratedNever();
        builder.Property(log => log.ActorUserId).IsRequired();
        builder.Property(log => log.TargetUserId).IsRequired();
        builder.Property(log => log.Action).IsRequired().HasMaxLength(64);
        builder.Property(log => log.OccurredAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");
        builder.Property(log => log.BeforeValue).HasMaxLength(1000);
        builder.Property(log => log.AfterValue).HasMaxLength(1000);
        builder.HasIndex(log => new { log.TargetUserId, log.OccurredAt });
    }
}
