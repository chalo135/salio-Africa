using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salio.Domain.Entities;

namespace Salio.Infrastructure.Configurations
{
    public class MembershipConfiguration : IEntityTypeConfiguration<Membership>
    {
        public void Configure(EntityTypeBuilder<Membership> builder)
        {
            builder.HasKey(m => m.Id);
            builder.Property(m => m.Id).ValueGeneratedNever();

            // One person holds one role in one shop. Two rows for the same
            // pair would leave no way to tell which role applies.
            builder.HasIndex(m => new { m.OrganizationId, m.AppUserId }).IsUnique();

            // Restrict on both: deleting an organisation or a person while
            // they still have a place in the books must fail loudly rather
            // than quietly removing who did what.
            builder.HasOne<Organization>()
                .WithMany()
                .HasForeignKey(m => m.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(m => m.AppUserId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
