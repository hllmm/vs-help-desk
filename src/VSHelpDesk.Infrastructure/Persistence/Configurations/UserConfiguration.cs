using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VSHelpDesk.Domain.Entities;

namespace VSHelpDesk.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(user => user.Id);

        builder.Property(user => user.Id).ValueGeneratedNever();
        builder.Property(user => user.FullName).IsRequired().HasMaxLength(200);
        builder.Property(user => user.Username).IsRequired().HasMaxLength(100);
        builder.Property(user => user.Email).IsRequired().HasMaxLength(255);
        builder.Property(user => user.PasswordHash).IsRequired();
        builder.Property(user => user.Role).IsRequired().HasConversion<int>();
        builder.Property(user => user.IsActive).IsRequired();
        builder.Property(user => user.CreatedAt).IsRequired().HasColumnType("timestamp with time zone");
        builder.Property(user => user.LastLoginAt).HasColumnType("timestamp with time zone");

        builder.HasIndex(user => user.Username).IsUnique();
    }
}
