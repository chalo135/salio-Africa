using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salio.Domain.Entities;

namespace Salio.Infrastructure.Configurations
{
    public class SaleLineConfiguration : IEntityTypeConfiguration<SaleLine>
    {
        public void Configure(EntityTypeBuilder<SaleLine> builder)
        {
            builder.HasKey(l => l.Id);
            builder.Property(l => l.Id).ValueGeneratedNever();

            builder.Property(l => l.Quantity).HasPrecision(18, 3);

            // Same lengths as Product, so any name a part can have fits.
            builder.Property(l => l.ProductName).IsRequired().HasMaxLength(200);
            builder.Property(l => l.Sku).IsRequired().HasMaxLength(50);

            builder.HasIndex(l => l.SaleId);
            builder.HasIndex(l => new { l.OrganizationId, l.ProductId });

            builder.HasOne<Sale>()
                .WithMany()
                .HasForeignKey(l => l.SaleId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<Product>()
                .WithMany()
                .HasForeignKey(l => l.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
