using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Persistence.Configurations;

public sealed class UserAuditEventConfiguration : IEntityTypeConfiguration<UserAuditEvent>
{
    public void Configure(EntityTypeBuilder<UserAuditEvent> builder)
    {
        builder.ToTable("UserAuditEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.ActorUserId).IsRequired();
        builder.Property(e => e.TargetUserId).IsRequired();
        builder.Property(e => e.EventType).IsRequired().HasMaxLength(32);
        builder.Property(e => e.BeforeRole).HasMaxLength(32);
        builder.Property(e => e.AfterRole).HasMaxLength(32);
        builder.Property(e => e.BeforeIsActive);
        builder.Property(e => e.AfterIsActive);
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.CorrelationId).HasMaxLength(64);
        builder.HasIndex(e => new { e.TargetUserId, e.CreatedAt });
        builder.HasIndex(e => e.ActorUserId);
    }
}
