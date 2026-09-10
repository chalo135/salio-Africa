using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salio.Domain.Entities;

namespace Salio.Infrastructure.Configurations
{
    public class ProductConfiguration : IEntityTypeConfiguration<Product>
    {
        public void Configure(EntityTypeBuilder<Product> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();

            builder.Property(p => p.Sku).IsRequired().HasMaxLength(50);
            builder.Property(p => p.Name).IsRequired().HasMaxLength(200);

            builder.HasIndex(p => new { p.OrganizationId, p.Sku }).IsUnique();

            // eTIMS — nullable, unused for now. Lengths are provisional.
            builder.Property(p => p.EtimsItemClsCd).HasMaxLength(20);
            builder.Property(p => p.EtimsPkgUnitCd).HasMaxLength(20);
            builder.Property(p => p.EtimsQtyUnitCd).HasMaxLength(20);
            builder.Property(p => p.EtimsOriginCountry).HasMaxLength(20);
        }
    }
}
