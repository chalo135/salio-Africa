using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salio.Domain.Entities;

namespace Salio.Infrastructure.Configurations
{
    public class SaleConfiguration : IEntityTypeConfiguration<Sale>
    {
        public void Configure(EntityTypeBuilder<Sale> builder)
        {
            builder.HasKey(s => s.Id);
            builder.Property(s => s.Id).ValueGeneratedNever();

            builder.Property(s => s.SaleNumber).IsRequired().HasMaxLength(50);

            // A receipt number has to be unique within the shop, or two sales
            // can claim the same one on a customer's copy.
            builder.HasIndex(s => new { s.OrganizationId, s.SaleNumber }).IsUnique();

            builder.HasIndex(s => new { s.OrganizationId, s.SaleDate });

            // "Show me my own sales" (locked rule 13). Restrict, so a person
            // who has sold anything can never be deleted out of the books.
            builder.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(s => s.AppUserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<Customer>()
                .WithMany()
                .HasForeignKey(s => s.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            // eTIMS — nullable, unused for now. Lengths are provisional.
            builder.Property(s => s.EtimsControlNumber).HasMaxLength(100);
            builder.Property(s => s.EtimsSignature).HasMaxLength(200);
            builder.Property(s => s.EtimsQr).HasMaxLength(500);
        }
    }
}
