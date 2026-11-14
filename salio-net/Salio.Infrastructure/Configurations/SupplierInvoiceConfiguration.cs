using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salio.Domain.Entities;

namespace Salio.Infrastructure.Configurations
{
    public class SupplierInvoiceConfiguration : IEntityTypeConfiguration<SupplierInvoice>
    {
        public void Configure(EntityTypeBuilder<SupplierInvoice> builder)
        {
            builder.HasKey(i => i.Id);
            builder.Property(i => i.Id).ValueGeneratedNever();

            builder.Property(i => i.ImagePath).IsRequired().HasMaxLength(500);
            builder.Property(i => i.SupplierName).HasMaxLength(200);
            builder.Property(i => i.InvoiceNumber).HasMaxLength(100);

            // RawExtractionJson has no length limit, so it is stored as text.

            // CommittedStockReceiptId is deliberately not a foreign key:
            // there is no receipts table to point at.

            builder.HasIndex(i => new { i.OrganizationId, i.UploadedAt });

            builder.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(i => i.UploadedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
