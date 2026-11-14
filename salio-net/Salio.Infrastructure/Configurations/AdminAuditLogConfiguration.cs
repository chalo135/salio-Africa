using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Salio.Domain.Entities;

namespace Salio.Infrastructure.Configurations
{
    // DELIBERATE, NAMED EXEMPTION FROM LOCKED RULE 2: the admin audit log is
    // platform-level, not tenant-level. Do not "fix" this by adding a
    // required OrganizationId.
    public class AdminAuditLogConfiguration : IEntityTypeConfiguration<AdminAuditLog>
    {
        public void Configure(EntityTypeBuilder<AdminAuditLog> builder)
        {
            builder.HasKey(a => a.Id);
            builder.Property(a => a.Id).ValueGeneratedNever();

            builder.Property(a => a.Action).IsRequired().HasMaxLength(50);
            builder.Property(a => a.Detail).IsRequired().HasMaxLength(500);

            // Restrict everywhere: nothing the log names may be deleted
            // while the log still points at it.
            builder.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(a => a.AdminUserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<Organization>()
                .WithMany()
                .HasForeignKey(a => a.TargetOrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne<SetupCode>()
                .WithMany()
                .HasForeignKey(a => a.TargetSetupCodeId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
