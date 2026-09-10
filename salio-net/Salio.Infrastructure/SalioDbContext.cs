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
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<TaxRate> TaxRates => Set<TaxRate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Finds every IEntityTypeConfiguration<T> in this project and applies
        // it. Without this line the configuration classes are dead code: no
        // keys, no indexes, no column lengths.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SalioDbContext).Assembly);
    }
}
