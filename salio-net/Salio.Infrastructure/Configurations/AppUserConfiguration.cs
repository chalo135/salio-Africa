using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salio.Domain.Entities;

namespace Salio.Infrastructure.Configurations
{
    public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
    {
        public void Configure(EntityTypeBuilder<AppUser> builder)
        {
            builder.HasKey(u => u.Id);
            builder.Property(u => u.Id).ValueGeneratedNever();

            builder.Property(u => u.FullName).IsRequired().HasMaxLength(200);
            builder.Property(u => u.Email).IsRequired().HasMaxLength(256);

            // Identity's hasher produces about 84 characters today. The column
            // is longer so a future, stronger format still fits.
            builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(256);

            // One account per email address. This is how a login finds its user.
            builder.HasIndex(u => u.Email).IsUnique();
        }
    }
}
