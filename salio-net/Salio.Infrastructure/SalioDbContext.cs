using Microsoft.EntityFrameworkCore;
using Salio.Domain.Entities;

namespace Salio.Infrastructure;

public class SalioDbContext : DbContext
{
    public SalioDbContext(DbContextOptions<SalioDbContext> options)
        : base(options)
    {
    }

    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SalioDbContext).Assembly);
    }
}
