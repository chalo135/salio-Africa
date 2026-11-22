using System.Text.RegularExpressions;
using Salio.Domain.Entities;
using Salio.Infrastructure;
using Xunit;

namespace Salio.Tests;

// Price history cannot be backfilled: a selling-price write that forgets to
// record itself loses that change for good. No database is used.
public class PriceChangeGuardTests
{
    [Fact]
    public void Every_file_that_sets_a_selling_price_records_it()
    {
        // "SellPriceMinor =" but not "==". Salio.Domain is left out: it has
        // no database and only builds import rows, never a saved Product.
        Regex setsPrice = new(@"\bSellPriceMinor\s*=(?!=)");
        Regex records = new(@"\bPriceChanges\s*\.\s*Record\s*\(");
        var problems = new List<string>();
        int found = 0;

        foreach (SourceCode.SourceFile file in SourceCode.ProductionFiles())
        {
            if (file.RelativePath.StartsWith("Salio.Domain/")
                || file.RelativePath.StartsWith("Salio.Infrastructure/Migrations/"))
            {
                continue;
            }

            if (!setsPrice.IsMatch(file.Code))
            {
                continue;
            }

            found++;

            if (!records.IsMatch(file.Code))
            {
                problems.Add(file.RelativePath + " sets SellPriceMinor but never calls PriceChanges.Record.");
            }
        }

        // ProductsController, ProductImportService, SupplierInvoiceService,
        // DemoProductSeeder. Fewer means the search is broken.
        Assert.True(found >= 4, "Found only " + found + " files setting a selling price.");
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void A_change_is_recorded_and_an_unchanged_price_is_not()
    {
        using var db = SalioModel.NewContext();
        var product = new Product { Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), SellPriceMinor = 25000 };
        Guid who = Guid.NewGuid();

        PriceChanges.Record(db, product, 25000, who);
        Assert.Empty(db.ChangeTracker.Entries<PriceChange>());

        PriceChanges.Record(db, product, 20000, who);
        PriceChange change = Assert.Single(db.ChangeTracker.Entries<PriceChange>()).Entity;
        Assert.Equal(20000, change.OldPriceMinor);
        Assert.Equal(25000, change.NewPriceMinor);
        Assert.Equal(who, change.ChangedByUserId);
        Assert.Equal(product.OrganizationId, change.OrganizationId);
    }

    [Fact]
    public void A_new_part_is_recorded_with_no_old_price()
    {
        using var db = SalioModel.NewContext();
        var product = new Product { Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), SellPriceMinor = 1500 };

        PriceChanges.Record(db, product, null, null);

        Assert.Null(Assert.Single(db.ChangeTracker.Entries<PriceChange>()).Entity.OldPriceMinor);
    }
}
