using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;
using Xunit;

namespace Salio.Tests;

// The welcome card's two stock questions. Amounts in minor units.
public class WelcomeCardRulesTests
{
    private static readonly DateTimeOffset Since = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
    private static readonly HashSet<Guid> NoneImported = new();

    private static StockMovement Move(StockMovementType type, decimal quantity, long cost, int hoursAfterSince, string? note = null)
    {
        return new StockMovement
        {
            Id = Guid.NewGuid(),
            Type = type,
            QuantityChange = quantity,
            UnitCostMinor = cost,
            OccurredAt = Since.AddHours(hoursAfterSince),
            Note = note
        };
    }

    [Fact]
    public void Engine_oil_delivered_at_450_after_350_is_a_rise()
    {
        var movements = new List<StockMovement>
        {
            Move(StockMovementType.Receipt, 10, 35000, -48),
            Move(StockMovementType.Sale, -10, 35000, -24),
            Move(StockMovementType.Receipt, 10, 45000, 5)
        };

        CostRise? rise = WelcomeCardRules.CostRoseSince(movements, NoneImported, Since);

        Assert.NotNull(rise);
        Assert.Equal(35000, rise.WasMinor);
        Assert.Equal(45000, rise.NowMinor);
    }

    [Fact]
    public void A_cheaper_or_older_delivery_is_not_a_rise()
    {
        var cheaper = new List<StockMovement>
        {
            Move(StockMovementType.Receipt, 10, 45000, -48),
            Move(StockMovementType.Receipt, 10, 35000, 5)
        };
        var beforeSince = new List<StockMovement>
        {
            Move(StockMovementType.Receipt, 10, 35000, -48),
            Move(StockMovementType.Receipt, 10, 45000, -2)
        };

        Assert.Null(WelcomeCardRules.CostRoseSince(cheaper, NoneImported, Since));
        Assert.Null(WelcomeCardRules.CostRoseSince(beforeSince, NoneImported, Since));
    }

    [Fact]
    public void An_imported_cost_is_never_the_was()
    {
        var imported = Move(StockMovementType.Adjustment, 10, 35000, -48, "Opening stock 1a2b3c4d");
        var movements = new List<StockMovement> { imported, Move(StockMovementType.Receipt, 10, 45000, 5) };

        Assert.Null(WelcomeCardRules.CostRoseSince(movements, new HashSet<Guid> { imported.Id }, Since));
    }

    [Fact]
    public void Typed_in_opening_stock_counts_as_the_was()
    {
        var movements = new List<StockMovement>
        {
            Move(StockMovementType.Adjustment, 10, 35000, -48, "Opening stock 1a2b3c4d"),
            Move(StockMovementType.Receipt, 10, 45000, 5)
        };

        Assert.Equal(35000, WelcomeCardRules.CostRoseSince(movements, NoneImported, Since)?.WasMinor);
    }

    [Fact]
    public void Ran_out_after_since_and_still_out()
    {
        var sale = Move(StockMovementType.Sale, -3, 0, 4);
        var movements = new List<StockMovement> { Move(StockMovementType.Receipt, 3, 1000, -48), sale };

        Assert.Equal(sale.OccurredAt, WelcomeCardRules.RanOutSince(movements, Since));
    }

    [Fact]
    public void Out_since_before_or_restocked_is_not_news()
    {
        var oldNews = new List<StockMovement>
        {
            Move(StockMovementType.Receipt, 3, 1000, -48),
            Move(StockMovementType.Sale, -3, 0, -24)
        };
        var restocked = new List<StockMovement>
        {
            Move(StockMovementType.Receipt, 3, 1000, -48),
            Move(StockMovementType.Sale, -3, 0, 2),
            Move(StockMovementType.Receipt, 5, 1000, 3)
        };

        Assert.Null(WelcomeCardRules.RanOutSince(oldNews, Since));
        Assert.Null(WelcomeCardRules.RanOutSince(restocked, Since));
    }

    [Fact]
    public void Selling_past_zero_counts_as_running_out()
    {
        var sale = Move(StockMovementType.Sale, -5, 0, 1);
        var movements = new List<StockMovement> { Move(StockMovementType.Receipt, 2, 1000, -48), sale };

        Assert.Equal(sale.OccurredAt, WelcomeCardRules.RanOutSince(movements, Since));
    }
}
