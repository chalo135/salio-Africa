using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salio.Domain.Entities;

namespace Salio.Infrastructure.Configurations
{
    public class AccountConfiguration : IEntityTypeConfiguration<Account>
    {
        public void Configure(EntityTypeBuilder<Account> builder)
        {
            builder.HasKey(a => a.Id);
            builder.Property(a => a.Id).ValueGeneratedNever();

            builder.Property(a => a.Code).IsRequired().HasMaxLength(20);
            builder.Property(a => a.Name).IsRequired().HasMaxLength(200);

            builder.HasIndex(a => new { a.OrganizationId, a.Code }).IsUnique();

            builder.HasOne<Account>()
                .WithMany()
                .HasForeignKey(a => a.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
