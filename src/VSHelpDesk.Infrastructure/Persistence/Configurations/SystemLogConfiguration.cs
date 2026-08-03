using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Persistence.Configurations;

public sealed class SystemLogConfiguration : IEntityTypeConfiguration<SystemLog>
{
    public void Configure(EntityTypeBuilder<SystemLog> builder)
    {
        builder.ToTable("SystemLogs");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.LogLevel).IsRequired().HasMaxLength(50);
        builder.Property(l => l.Message).IsRequired();
        builder.Property(l => l.Exception);
        builder.Property(l => l.CategoryName).IsRequired().HasMaxLength(250);
        builder.Property(l => l.EventId);
        builder.Property(l => l.CreatedAt).IsRequired().HasColumnType("timestamp with time zone");
    }
}
