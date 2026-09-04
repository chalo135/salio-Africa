using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salio.Domain.Entities;

namespace Salio.Infrastructure.Configurations
{
    public class JournalLineConfiguration : IEntityTypeConfiguration<JournalLine>
    {
        public void Configure(EntityTypeBuilder<JournalLine> builder)
        {
            builder.HasKey(l => l.Id);
            builder.Property(l => l.Id).ValueGeneratedNever();

            builder.Property(l => l.Currency).IsRequired().HasMaxLength(3);

            builder.HasIndex(l => l.JournalEntryId);
            builder.HasIndex(l => new { l.OrganizationId, l.AccountId });

            builder.HasOne<JournalEntry>()
                .WithMany()
                .HasForeignKey(l => l.JournalEntryId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<Account>()
                .WithMany()
                .HasForeignKey(l => l.AccountId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
