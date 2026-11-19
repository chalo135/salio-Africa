using System.Text.Json;
using Salio.Api.Controllers;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;
using Xunit;

namespace Salio.Tests;

// What the shelf list sends to the browser. The cost must be ABSENT from the
// JSON when the gate says no: not null, not hidden by the screen. A cost sent
// and hidden with CSS is still on the cashier's phone.
public class ProductCostVisibilityTests
{
    private static readonly Guid Shop = Guid.NewGuid();

    // KES 500 selling price, ten received at KES 450 each.
    private static (Product product, List<StockMovement> movements) OilWithStock()
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            OrganizationId = Shop,
            Sku = "OIL-1",
            Name = "Engine oil 1L",
            SellPriceMinor = 50000
        };

        var movements = new List<StockMovement>
        {
            new()
            {
                Id = Guid.NewGuid(),
                OrganizationId = Shop,
                ProductId = product.Id,
                Type = StockMovementType.Receipt,
                QuantityChange = 10,
                UnitCostMinor = 45000,
                OccurredAt = DateTimeOffset.UtcNow
            }
        };

        return (product, movements);
    }

    private static JsonElement ShelfLineJson(ProductsController.PricingView pricing)
    {
        var (product, movements) = OilWithStock();
        var response = ProductsController.BuildResponse(
            product, movements, new StockService(), pricing, new HashSet<Guid>());

        // The same naming the API uses: ASP.NET's web defaults (camelCase).
        string json = JsonSerializer.Serialize(response, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public void Cashier_with_the_shop_setting_off_gets_no_cost_key_at_all()
    {
        // Through the real gate, as ProductsController asks it.
        var context = new CostVisibilityContext(
            MembershipRole.Cashier,
            OrganizationAllowsCashierCost: false,
            IsActiveMember: true,
            PlatformAdmin: false);

        var pricing = new ProductsController.PricingView(
            CostVisibilityRules.CanSeeCost(context), MinMarginPercent: 15);

        JsonElement line = ShelfLineJson(pricing);

        foreach (JsonProperty property in line.EnumerateObject())
        {
            Assert.DoesNotContain("cost", property.Name, StringComparison.OrdinalIgnoreCase);
        }

        Assert.False(line.TryGetProperty("averageCostMinor", out _));
    }

    [Fact]
    public void The_stop_price_is_sent_even_when_the_cost_is_not()
    {
        JsonElement line = ShelfLineJson(new ProductsController.PricingView(CanSeeCost: false, MinMarginPercent: 15));

        // 450 + 15% = 517.50, up to 520.
        Assert.Equal(52000, line.GetProperty("stopPriceMinor").GetInt64());
        Assert.False(line.GetProperty("buyingPriceShown").GetBoolean());
    }

    [Fact]
    public void When_the_gate_says_yes_the_cost_is_sent()
    {
        JsonElement line = ShelfLineJson(new ProductsController.PricingView(CanSeeCost: true, MinMarginPercent: 15));

        Assert.Equal(45000, line.GetProperty("averageCostMinor").GetInt64());
        Assert.True(line.GetProperty("buyingPriceShown").GetBoolean());
    }
}
