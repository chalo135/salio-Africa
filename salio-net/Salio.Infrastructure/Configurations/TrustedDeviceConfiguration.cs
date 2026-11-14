using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salio.Domain.Entities;

namespace Salio.Infrastructure.Configurations
{
    public class TrustedDeviceConfiguration : IEntityTypeConfiguration<TrustedDevice>
    {
        public void Configure(EntityTypeBuilder<TrustedDevice> builder)
        {
            builder.HasKey(d => d.Id);
            builder.Property(d => d.Id).ValueGeneratedNever();

            // Same room as AppUser.PasswordHash: these are hashes from the
            // same hasher.
            builder.Property(d => d.SecretHash).IsRequired().HasMaxLength(256);
            builder.Property(d => d.PinHash).HasMaxLength(256);

            // A Guid written as text is 36 characters.
            builder.Property(d => d.SecurityStamp).IsRequired().HasMaxLength(64);

            // Restrict: removing a person who still has devices must fail
            // loudly. Devices are revoked, never deleted.
            builder.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(d => d.AppUserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(d => d.AppUserId);
        }
    }
}
