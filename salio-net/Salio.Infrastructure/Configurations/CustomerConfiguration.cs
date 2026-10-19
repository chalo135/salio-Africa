using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salio.Domain.Entities;

namespace Salio.Infrastructure.Configurations
{
    public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
    {
        public void Configure(EntityTypeBuilder<Customer> builder)
        {
            builder.HasKey(c => c.Id);
            builder.Property(c => c.Id).ValueGeneratedNever();

            builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
            builder.Property(c => c.Phone).IsRequired().HasMaxLength(30);

            // One customer per phone number in a shop, so a returning rider
            // cannot end up with two separate balances.
            builder.HasIndex(c => new { c.OrganizationId, c.Phone }).IsUnique();

            // eTIMS / KRA — nullable, unused for now. Lengths are provisional.
            builder.Property(c => c.KraPin).HasMaxLength(20);
            builder.Property(c => c.EtimsBranchCd).HasMaxLength(10);
        }
    }
}
