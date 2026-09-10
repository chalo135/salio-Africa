namespace Salio.Domain.Services;

using Salio.Domain.Entities;

public class StockService
{
    public decimal CurrentQuantity(List<StockMovement> movements)
    {
        decimal total = 0;

        foreach (var movement in movements)
        {
            total = total + movement.QuantityChange;
        }

        return total;
    }

    public long CalculateWeightedAverageCost(List<StockMovement> movements)
    {
        if (movements.Count == 0)
        {
            return 0;
        }

        // A weighted average depends on the order things happened: stock has to
        // be bought before it can be sold. The caller may hand us rows in any
        // order, so we sort here rather than trust them. OrderBy returns a new
        // sequence, so the caller's list is left alone.
        var ordered = movements.OrderBy(m => m.OccurredAt);

        decimal runningQuantity = 0;
        decimal runningValueMinor = 0;

        foreach (var movement in ordered)
        {
            if (movement.QuantityChange == 0)
            {
                continue;
            }

            if (movement.QuantityChange > 0)
            {
                decimal unitCost = 0;

                if (movement.UnitCostMinor > 0)
                {
                    unitCost = movement.UnitCostMinor;
                }
                else if (runningQuantity > 0)
                {
                    unitCost = runningValueMinor / runningQuantity;
                }

                runningValueMinor = runningValueMinor + (movement.QuantityChange * unitCost);
                runningQuantity = runningQuantity + movement.QuantityChange;
            }
            else
            {
                // Stock going out leaves at the average of what is on the shelf,
                // not at what any one delivery cost. Once two deliveries are
                // mixed together we can no longer tell which unit was sold.
                if (runningQuantity > 0)
                {
                    decimal averageNow = runningValueMinor / runningQuantity;
                    runningValueMinor = runningValueMinor + (movement.QuantityChange * averageNow);
                }

                runningQuantity = runningQuantity + movement.QuantityChange;
            }

            // Selling out clears the shelf. Without this, tiny rounding
            // remainders would survive as value attached to no units at all.
            if (runningQuantity <= 0)
            {
                runningValueMinor = 0;
            }
        }

        // No stock means no cost per unit, and it keeps us from dividing by zero.
        if (runningQuantity <= 0)
        {
            return 0;
        }

        // Round once, here. Rounding inside the loop would compound the error
        // on every movement.
        return (long)Math.Round(runningValueMinor / runningQuantity, MidpointRounding.AwayFromZero);
    }
}
