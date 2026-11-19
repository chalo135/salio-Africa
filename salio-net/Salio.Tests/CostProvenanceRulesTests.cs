using System.Text.Json;
using Salio.Api.Controllers;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;
using Xunit;

namespace Salio.Tests;

// A cost a person stated is confirmed; a cost an import carried in is not,
// and gives no stop price. Amounts in minor units: KES 450 is 45000.
public class CostProvenanceRulesTests
{
    private static readonly Guid Shop = Guid.NewGuid();
    private static readonly Guid Part = Guid.NewGuid();
    private static readonly HashSet<Guid> NoImports = new();

    private static StockMovement In(StockMovementType type, string note, long unitCostMinor, decimal quantity = 10)
    {
        return new StockMovement
        {
            Id = Guid.NewGuid(),
            OrganizationId = Shop,
            ProductId = Part,
            Type = type,
            QuantityChange = quantity,
            UnitCostMinor = unitCostMinor,
            OccurredAt = DateTimeOffset.UtcNow,
            Note = note
        };
    }

    private static StockMovement Imported() =>
        In(StockMovementType.Adjustment, "Opening stock 1a2b3c4d", 31500);

    private static StockMovement Purchased() =>
        In(StockMovementType.Receipt, "Stock receipt 9f8e7d6c from Kamau Wholesale", 45000);

    [Fact]
    public void A_part_with_only_imported_movements_is_unconfirmed()
    {
        var imported = Imported();

        Assert.False(CostProvenanceRules.CostIsConfirmed(
            new[] { imported }, new HashSet<Guid> { imported.Id }));
    }

    [Fact]
    public void A_part_with_a_real_purchase_is_confirmed()
    {
        Assert.True(CostProvenanceRules.CostIsConfirmed(new[] { Purchased() }, NoImports));
    }

    [Fact]
    public void A_part_with_no_movements_is_unconfirmed()
    {
        Assert.False(CostProvenanceRules.CostIsConfirmed(Array.Empty<StockMovement>(), NoImports));
    }

    [Fact]
    public void Opening_stock_a_person_typed_in_is_confirmed()
    {
        // The same movement type as an import's; only its origin differs.
        Assert.True(CostProvenanceRules.CostIsConfirmed(
            new[] { In(StockMovementType.Adjustment, "Opening stock 5e6f7a8b", 45000) }, NoImports));
    }

    [Fact]
    public void A_real_purchase_on_top_of_unreversed_imported_stock_is_still_unconfirmed()
    {
        // The average still blends the invented cost in.
        var imported = Imported();

        Assert.False(CostProvenanceRules.CostIsConfirmed(
            new[] { imported, Purchased() }, new HashSet<Guid> { imported.Id }));
    }

    [Fact]
    public void Once_the_import_is_reversed_a_real_purchase_confirms_the_cost()
    {
        // Reversed imports are not in the live set.
        Assert.True(CostProvenanceRules.CostIsConfirmed(new[] { Imported(), Purchased() }, NoImports));
    }

    [Fact]
    public void A_stock_count_surplus_is_not_a_stated_cost()
    {
        Assert.False(CostProvenanceRules.CostIsConfirmed(
            new[] { In(StockMovementType.Adjustment, "Stock count 0a1b2c3d", 31500, 2) }, NoImports));
    }

    [Fact]
    public void Sales_alone_state_no_cost()
    {
        Assert.False(CostProvenanceRules.CostIsConfirmed(
            new[] { In(StockMovementType.Sale, "S-000001", 0, -1) }, NoImports));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(100)]
    public void An_unconfirmed_part_yields_a_null_stop_whatever_the_margin(int margin)
    {
        var product = new Product
        {
            Id = Part,
            OrganizationId = Shop,
            Sku = "OIL-1",
            Name = "Engine oil 1L",
            SellPriceMinor = 50000
        };
        var imported = Imported();

        var response = ProductsController.BuildResponse(
            product,
            new List<StockMovement> { imported },
            new StockService(),
            new ProductsController.PricingView(CanSeeCost: true, MinMarginPercent: margin),
            new HashSet<Guid> { imported.Id });

        Assert.Null(response.StopPriceMinor);

        // Nothing is hidden from someone allowed to see cost: the figure the
        // books hold is still there, marked as not confirmed.
        Assert.Equal(31500, response.AverageCostMinor);
        Assert.False(response.CostIsConfirmed);
    }

    [Fact]
    public void Whether_a_cost_is_confirmed_is_not_sent_to_someone_who_may_not_see_cost()
    {
        var product = new Product { Id = Part, OrganizationId = Shop, Sku = "X", Name = "X", SellPriceMinor = 50000 };

        var response = ProductsController.BuildResponse(
            product,
            new List<StockMovement> { Purchased() },
            new StockService(),
            new ProductsController.PricingView(CanSeeCost: false, MinMarginPercent: 15),
            NoImports);

        string json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("costIsConfirmed", json);
        Assert.DoesNotContain("averageCostMinor", json);
    }

    [Fact]
    public void Opening_stock_still_writes_the_note_this_rule_reads()
    {
        // CostProvenanceRules and ImportedCostFinder recognise opening stock by
        // its note. If OpeningStockWriter ever writes a different note, every
        // opening stock would silently read as unconfirmed.
        string writer = SourceCode.ProductionFile("Salio.Infrastructure/OpeningStockWriter.cs").CodeWithStrings;

        Assert.Contains("\"" + CostProvenanceRules.OpeningStockNotePrefix + "\"", writer);
    }
}
