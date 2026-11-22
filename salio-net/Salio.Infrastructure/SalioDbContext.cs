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
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<FiledReturn> FiledReturns => Set<FiledReturn>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<TrustedDevice> TrustedDevices => Set<TrustedDevice>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<SetupCode> SetupCodes => Set<SetupCode>();
    public DbSet<AdminAuditLog> AdminAuditLogs => Set<AdminAuditLog>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportBatchChange> ImportBatchChanges => Set<ImportBatchChange>();
    public DbSet<SupplierInvoice> SupplierInvoices => Set<SupplierInvoice>();
    public DbSet<SupplierInvoiceLine> SupplierInvoiceLines => Set<SupplierInvoiceLine>();
        public DbSet<PriceChange> PriceChanges => Set<PriceChange>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Finds every IEntityTypeConfiguration<T> in this project and applies
        // it. Without this line the configuration classes are dead code: no
        // keys, no indexes, no column lengths.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SalioDbContext).Assembly);
    }
}
