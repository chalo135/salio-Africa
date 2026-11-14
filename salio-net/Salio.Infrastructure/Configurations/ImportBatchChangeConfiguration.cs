using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salio.Domain.Entities;

namespace Salio.Infrastructure.Configurations
{
    public class ImportBatchChangeConfiguration : IEntityTypeConfiguration<ImportBatchChange>
    {
        public void Configure(EntityTypeBuilder<ImportBatchChange> builder)
        {
            builder.HasKey(c => c.Id);
            builder.Property(c => c.Id).ValueGeneratedNever();

            // PreviousValuesJson has no length limit, so it is stored as text.

            // Undo loads every change in one batch.
            builder.HasIndex(c => new { c.OrganizationId, c.ImportBatchId });

            builder.HasOne<ImportBatch>()
                .WithMany()
                .HasForeignKey(c => c.ImportBatchId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<Product>()
                .WithMany()
                .HasForeignKey(c => c.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
