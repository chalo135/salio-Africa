using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salio.Domain.Entities;

namespace Salio.Infrastructure.Configurations
{
    // DELIBERATE, NAMED EXEMPTION FROM LOCKED RULE 2: setup codes are
    // platform-level, not tenant-level. A code exists before the shop it
    // creates. Do not "fix" this by making OrganizationId required.
    public class SetupCodeConfiguration : IEntityTypeConfiguration<SetupCode>
    {
        public void Configure(EntityTypeBuilder<SetupCode> builder)
        {
            builder.HasKey(c => c.Id);
            builder.Property(c => c.Id).ValueGeneratedNever();

            // New codes are six characters. The column stays at 32 so that
            // older, longer codes (now revoked) are kept rather than lost.
            builder.Property(c => c.Code).IsRequired().HasMaxLength(32);
            builder.HasIndex(c => c.Code).IsUnique();

            builder.Property(c => c.ShopName).IsRequired().HasMaxLength(200);
            builder.Property(c => c.Email).IsRequired().HasMaxLength(256).HasDefaultValue("");

            // Set only once redeemed. Restrict: a shop that a code made
            // cannot be deleted out from under the code.
            builder.HasOne<Organization>()
                .WithMany()
                .HasForeignKey(c => c.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
