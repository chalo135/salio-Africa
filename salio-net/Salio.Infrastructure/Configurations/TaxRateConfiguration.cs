using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salio.Domain.Entities;

namespace Salio.Infrastructure.Configurations
{
    public class TaxRateConfiguration : IEntityTypeConfiguration<TaxRate>
    {
        public void Configure(EntityTypeBuilder<TaxRate> builder)
        {
            builder.HasKey(r => r.Id);
            builder.Property(r => r.Id).ValueGeneratedNever();

            // 16.00 fits; so does 0.00 for a zero-rated band.
            builder.Property(r => r.RatePercent).HasPrecision(5, 2);

            builder.Property(r => r.Source).IsRequired().HasMaxLength(200);

            // Looking up "the rate for band A on this date" is the only query
            // this table has.
            builder.HasIndex(r => new { r.OrganizationId, r.Band, r.EffectiveFrom });
        }
    }
}
