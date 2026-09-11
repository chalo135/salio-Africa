using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salio.Domain.Entities;

namespace Salio.Infrastructure.Configurations
{
    public class ShiftConfiguration : IEntityTypeConfiguration<Shift>
    {
        public void Configure(EntityTypeBuilder<Shift> builder)
        {
            builder.HasKey(s => s.Id);
            builder.Property(s => s.Id).ValueGeneratedNever();

            builder.Property(s => s.OpenedBy).IsRequired().HasMaxLength(100);

            // "Which shift is open at this till right now" is the query the
            // till runs before it will let anyone sell anything.
            builder.HasIndex(s => new { s.OrganizationId, s.Status });

            builder.HasIndex(s => new { s.OrganizationId, s.OpenedAt });
        }
    }
}
