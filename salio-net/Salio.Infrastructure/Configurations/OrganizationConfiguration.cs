using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salio.Domain.Entities;

namespace Salio.Infrastructure.Configurations
{
    // Only the setup fields are configured here. Everything older is left to
    // EF's conventions, as it always was, so the migration touches nothing
    // else.
    public class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
    {
        public void Configure(EntityTypeBuilder<Organization> builder)
        {
            // "Sales" or "Services". Shops made before this column existed
            // were converted from the old HoldsStock flag by the migration.
            builder.Property(o => o.BusinessKind).IsRequired().HasMaxLength(20).HasDefaultValue("Sales");

            builder.Property(o => o.Trade).IsRequired().HasMaxLength(120).HasDefaultValue("");

            builder.Property(o => o.SellableNoun).IsRequired().HasMaxLength(30).HasDefaultValue("Parts");
        }
    }
}
