using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salio.Domain.Entities;

namespace Salio.Infrastructure.Configurations
{
    public class SupplierInvoiceLineConfiguration : IEntityTypeConfiguration<SupplierInvoiceLine>
    {
        public void Configure(EntityTypeBuilder<SupplierInvoiceLine> builder)
        {
            builder.HasKey(l => l.Id);
            builder.Property(l => l.Id).ValueGeneratedNever();

            builder.Property(l => l.RawText).IsRequired().HasMaxLength(1000);
            builder.Property(l => l.ProposedName).IsRequired().HasMaxLength(200);

            // Same precision as StockMovement.QuantityChange.
            builder.Property(l => l.ProposedQuantity).HasPrecision(18, 3);

            // Each line number appears once per invoice.
            builder.HasIndex(l => new { l.SupplierInvoiceId, l.LineNumber }).IsUnique();

            builder.HasOne<SupplierInvoice>()
                .WithMany()
                .HasForeignKey(l => l.SupplierInvoiceId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<Product>()
                .WithMany()
                .HasForeignKey(l => l.MatchedProductId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
