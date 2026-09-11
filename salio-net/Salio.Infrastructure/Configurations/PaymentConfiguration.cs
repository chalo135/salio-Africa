using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salio.Domain.Entities;

namespace Salio.Infrastructure.Configurations
{
    public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
    {
        public void Configure(EntityTypeBuilder<Payment> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();

            // An M-Pesa confirmation code is about ten characters. Nullable,
            // because a cash payment has nothing to reference.
            builder.Property(p => p.Reference).HasMaxLength(50);

            // Every payment on one receipt.
            builder.HasIndex(p => p.SaleId);

            // Cash taken during a shift or a day.
            builder.HasIndex(p => new { p.OrganizationId, p.ReceivedAt });

            builder.HasOne<Sale>()
                .WithMany()
                .HasForeignKey(p => p.SaleId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
