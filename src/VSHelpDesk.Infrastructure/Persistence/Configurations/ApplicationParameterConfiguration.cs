using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Persistence.Configurations;

public sealed class ApplicationParameterConfiguration : IEntityTypeConfiguration<ApplicationParameter>
{
    public void Configure(EntityTypeBuilder<ApplicationParameter> builder)
    {
        builder.ToTable("ApplicationParameters");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Key).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Value).IsRequired().HasMaxLength(4000);
        builder.Property(p => p.Description).IsRequired().HasMaxLength(1000);
        builder.Property(p => p.UpdatedAt).IsRequired().HasColumnType("timestamp with time zone");
        builder.HasIndex(p => p.Key).IsUnique();
    }
}
