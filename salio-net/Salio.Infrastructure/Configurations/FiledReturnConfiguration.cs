using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salio.Domain.Entities;

namespace Salio.Infrastructure.Configurations
{
    public class FiledReturnConfiguration : IEntityTypeConfiguration<FiledReturn>
    {
        public void Configure(EntityTypeBuilder<FiledReturn> builder)
        {
            builder.HasKey(r => r.Id);
            builder.Property(r => r.Id).ValueGeneratedNever();

            builder.Property(r => r.AcknowledgementReference).IsRequired().HasMaxLength(100);

            // Every journal post asks "is this date inside a filed period?"
            builder.HasIndex(r => new { r.OrganizationId, r.PeriodFrom, r.PeriodTo });
        }
    }
}
