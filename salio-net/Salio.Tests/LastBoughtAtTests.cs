using Salio.Api.Controllers;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;

namespace Salio.Tests;

// "Last bought at": what a person last paid for one. It survives selling
// out, ignores imported costs, and is never used to post anything.
// Amounts in minor units: KES 800 is 80000.
public class LastBoughtAtTests
{
    private static readonly Guid Shop = Guid.NewGuid();
    private static readonly Guid Part = Guid.NewGuid();
    private static readonly HashSet<Guid> NoImports = new();
    private static readonly DateTimeOffset Monday = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    private static StockMovement Move(
        StockMovementType type, decimal quantity, long unitCostMinor, int day, string note = "Stock receipt 9f8e7d6c")
    {
        return new StockMovement
        {
            Id = Guid.NewGuid(),
            OrganizationId = Shop,
            ProductId = Part,
            Type = type,
            QuantityChange = quantity,
            UnitCostMinor = unitCostMinor,
            OccurredAt = Monday.AddDays(day),
            Note = note
        };
    }

    [Fact]
    public void It_is_the_newest_stated_cost()
    {
        var movements = new[]
        {
            Move(StockMovementType.Receipt, 2, 80000, day: 0),
            Move(StockMovementType.Receipt, 2, 100000, day: 1)
        };

        Assert.Equal(100000, CostProvenanceRules.LastBoughtAtMinor(movements, NoImports));
    }

    [Fact]
    public void It_survives_selling_out()
    {
        var movements = new[]
        {
            Move(StockMovementType.Receipt, 1, 80000, day: 0),
            Move(StockMovementType.Sale, -1, 80000, day: 1, note: "S-000001")
        };

        Assert.Equal(80000, CostProvenanceRules.LastBoughtAtMinor(movements, NoImports));
    }

    [Fact]
    public void Opening_stock_counts()
    {
        var movements = new[]
        {
            Move(StockMovementType.Adjustment, 3, 45000, day: 0, note: "Opening stock 1a2b3c4d")
        };

        Assert.Equal(45000, CostProvenanceRules.LastBoughtAtMinor(movements, NoImports));
    }

    [Fact]
    public void An_imported_cost_never_counts()
    {
        var imported = Move(StockMovementType.Adjustment, 5, 31500, day: 1, note: "Opening stock 1a2b3c4d");

        Assert.Null(CostProvenanceRules.LastBoughtAtMinor(new[] { imported }, new HashSet<Guid> { imported.Id }));
    }

    [Fact]
    public void A_real_purchase_on_top_of_imported_stock_still_counts()
    {
        var imported = Move(StockMovementType.Adjustment, 5, 31500, day: 0, note: "Opening stock 1a2b3c4d");
        var bought = Move(StockMovementType.Receipt, 2, 45000, day: 1);

        Assert.Equal(45000, CostProvenanceRules.LastBoughtAtMinor(
            new[] { imported, bought }, new HashSet<Guid> { imported.Id }));
    }

    [Fact]
    public void A_stock_count_surplus_is_not_a_purchase()
    {
        var movements = new[]
        {
            Move(StockMovementType.Adjustment, 2, 31500, day: 0, note: "Stock count 0a1b2c3d")
        };

        Assert.Null(CostProvenanceRules.LastBoughtAtMinor(movements, NoImports));
    }

    [Fact]
    public void The_shelf_list_uses_it_for_the_stop_after_selling_out()
    {
        var product = new Product { Id = Part, OrganizationId = Shop, Sku = "OIL", Name = "Oil", SellPriceMinor = 150000 };
        var movements = new List<StockMovement>
        {
            Move(StockMovementType.Receipt, 1, 100000, day: 0),
            Move(StockMovementType.Sale, -1, 100000, day: 1, note: "S-000001")
        };

        var response = ProductsController.BuildResponse(
            product, movements, new StockService(),
            new ProductsController.PricingView(CanSeeCost: true, MinMarginPercent: 15),
            NoImports);

        Assert.Equal(100000, response.LastBoughtAtMinor);
        // 1,000 plus 15% is 1,150.
        Assert.Equal(115000, response.StopPriceMinor);
    }

    [Fact]
    public void Cost_of_sales_still_posts_the_weighted_average()
    {
        // Stocked in twice at different prices, then one sold.
        var movements = new List<StockMovement>
        {
            Move(StockMovementType.Receipt, 2, 80000, day: 0),
            Move(StockMovementType.Receipt, 2, 100000, day: 1)
        };

        // What SalesController puts on the sale line: the weighted average,
        // (2 x 800 + 2 x 1,000) / 4 = 900. The same figure as before last
        // bought at existed.
        long unitCostMinor = new StockService().CalculateWeightedAverageCost(movements);
        Assert.Equal(90000, unitCostMinor);

        var sale = new Sale
        {
            Id = Guid.NewGuid(),
            OrganizationId = Shop,
            TotalMinor = 150000,
            OccurredAt = Monday.AddDays(2)
        };
        var line = new SaleLine
        {
            Id = Guid.NewGuid(),
            OrganizationId = Shop,
            SaleId = sale.Id,
            ProductId = Part,
            Quantity = 1,
            UnitPriceMinor = 150000,
            LineTotalMinor = 150000,
            UnitCostMinor = unitCostMinor,
            TaxBand = 'A'
        };
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrganizationId = Shop,
            SaleId = sale.Id,
            Method = PaymentMethod.Cash,
            AmountMinor = 150000,
            ReceivedAt = Monday.AddDays(2)
        };

        Guid cash = Guid.NewGuid(), mpesa = Guid.NewGuid(), sales = Guid.NewGuid();
        Guid cogs = Guid.NewGuid(), inventory = Guid.NewGuid(), receivable = Guid.NewGuid();

        var lines = new SaleService().BuildJournalLines(
            sale, new List<SaleLine> { line }, new List<Payment> { payment },
            cash, mpesa, sales, cogs, inventory, receivable, null);

        var cogsLine = Assert.Single(lines, l => l.AccountId == cogs);
        Assert.Equal(90000, cogsLine.DebitMinor);
        Assert.Equal(0, cogsLine.CreditMinor);

        var inventoryLine = Assert.Single(lines, l => l.AccountId == inventory);
        Assert.Equal(90000, inventoryLine.CreditMinor);

        // Last bought at is 1,000, and it is not what was posted.
        Assert.Equal(100000, CostProvenanceRules.LastBoughtAtMinor(movements, NoImports));
    }

    [Fact]
    public void Nothing_that_posts_reads_last_bought_at()
    {
        // Only the shelf list (guidance) and the rule itself may name it.
        var allowed = new[]
        {
            "Salio.Domain/Services/CostProvenanceRules.cs",
            "Salio.Api/Controllers/ProductsController.cs",
            "Salio.Api/Dtos/ProductStockResponse.cs"
        };

        var offenders = SourceCode.ProductionFiles()
            .Where(f => f.Code.Contains("LastBoughtAt"))
            .Select(f => f.RelativePath.Replace('\\', '/'))
            .Where(p => !allowed.Contains(p))
            .ToList();

        Assert.Empty(offenders);
    }
}
