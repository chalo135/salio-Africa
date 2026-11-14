using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salio.Domain.Entities;

namespace Salio.Infrastructure.Configurations
{
    public class ImportBatchConfiguration : IEntityTypeConfiguration<ImportBatch>
    {
        public void Configure(EntityTypeBuilder<ImportBatch> builder)
        {
            builder.HasKey(b => b.Id);
            builder.Property(b => b.Id).ValueGeneratedNever();

            builder.Property(b => b.FileName).IsRequired().HasMaxLength(255);

            // Both enums are stored as numbers, like every other enum here.
            builder.Property(b => b.StockBasis).IsRequired();
            builder.Property(b => b.PaidWith);

            // The import history screen lists a shop's batches, newest first.
            builder.HasIndex(b => new { b.OrganizationId, b.StartedAt });

            builder.HasOne<Organization>()
                .WithMany()
                .HasForeignKey(b => b.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(b => b.UploadedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(b => b.UndoneByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
