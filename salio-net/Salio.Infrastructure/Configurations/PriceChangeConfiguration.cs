using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salio.Domain.Entities;

namespace Salio.Infrastructure.Configurations
{
    public class PriceChangeConfiguration : IEntityTypeConfiguration<PriceChange>
    {
        public void Configure(EntityTypeBuilder<PriceChange> builder)
        {
            builder.HasKey(c => c.Id);
            builder.Property(c => c.Id).ValueGeneratedNever();

            // The welcome card asks "what changed in this shop since then?".
            builder.HasIndex(c => new { c.OrganizationId, c.ChangedAt });

            builder.HasOne<Product>()
                .WithMany()
                .HasForeignKey(c => c.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
