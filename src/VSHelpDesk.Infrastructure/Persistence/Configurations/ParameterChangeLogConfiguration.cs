using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Persistence.Configurations;

public sealed class ParameterChangeLogConfiguration : IEntityTypeConfiguration<ParameterChangeLog>
{
    public void Configure(EntityTypeBuilder<ParameterChangeLog> builder)
    {
        builder.ToTable("ParameterChangeLogs");
        builder.HasKey(log => log.Id);
        builder.Property(log => log.Id).ValueGeneratedNever();
        builder.Property(log => log.ParameterKey).IsRequired().HasMaxLength(200);
        builder.Property(log => log.OldValue).IsRequired().HasMaxLength(4000);
        builder.Property(log => log.NewValue).IsRequired().HasMaxLength(4000);
        builder.Property(log => log.ChangedByUserId).IsRequired();
        builder.Property(log => log.ChangedAt).IsRequired().HasColumnType("timestamp with time zone");
        builder.HasIndex(log => new { log.ParameterKey, log.ChangedAt });
    }
}
