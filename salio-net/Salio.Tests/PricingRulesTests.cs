using Salio.Domain.Services;
using Xunit;

namespace Salio.Tests;

// Every amount here is in minor units: KES 450 is 45000.
public class PricingRulesTests
{
    [Fact]
    public void Null_cost_yields_a_null_stop()
    {
        Assert.Null(PricingRules.StopPrice(minPriceMinor: null, unitCostMinor: null, minMarginPercent: 15));
    }

    [Fact]
    public void Zero_cost_is_not_known_and_yields_a_null_stop()
    {
        Assert.Null(PricingRules.StopPrice(minPriceMinor: null, unitCostMinor: 0, minMarginPercent: 15));
    }

    [Fact]
    public void Cost_450_margin_15_stops_at_520_above_the_500_selling_price()
    {
        // 450 * 1.15 = 517.50, up to 520. Not clamped to the selling price.
        Assert.Equal(52000, PricingRules.StopPrice(null, 45000, 15));
    }

    [Fact]
    public void Cost_365_margin_15_stops_at_420()
    {
        // 365 * 1.15 = 419.75, up to 420.
        Assert.Equal(42000, PricingRules.StopPrice(null, 36500, 15));
    }

    [Theory]
    [InlineData(40000, 0, 40000)]   // already on a 10 KES mark: stays
    [InlineData(40001, 0, 41000)]   // one cent over: up to the next mark
    [InlineData(39900, 0, 40000)]
    [InlineData(100, 15, 1000)]     // KES 1.15 rounds up to KES 10
    public void Rounds_up_to_the_nearest_10_KES(long cost, int margin, long expected)
    {
        Assert.Equal(expected, PricingRules.StopPrice(null, cost, margin));
    }

    [Fact]
    public void An_explicit_minimum_price_wins_even_below_cost()
    {
        Assert.Equal(30000, PricingRules.StopPrice(minPriceMinor: 30000, unitCostMinor: 45000, minMarginPercent: 15));
    }

    [Fact]
    public void An_explicit_minimum_price_wins_when_cost_is_unknown()
    {
        Assert.Equal(30000, PricingRules.StopPrice(minPriceMinor: 30000, unitCostMinor: null, minMarginPercent: 15));
    }

    [Fact]
    public void Uses_integer_arithmetic_only()
    {
        // A pricing rule with a decimal or a double in it would let
        // rounding drift into money (locked rule 1).
        string source = SourceCode.ProductionFile("Salio.Domain/Services/PricingRules.cs").Code;

        Assert.DoesNotContain("decimal", source);
        Assert.DoesNotContain("double", source);
        Assert.DoesNotContain("float", source);
        Assert.DoesNotContain("Math.", source);
    }
}
