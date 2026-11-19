namespace Salio.Domain.Services;

// Prices worked out from cost. Whole numbers only: money is long, in minor
// units (KES cents), and nothing here goes near floating point.
public static class PricingRules
{
    // 10 KES, in minor units. Stop prices are rounded UP to this.
    private const long RoundToMinor = 1000;

    // The lowest price a part should be sold at.
    //
    //   the owner's own MinPriceMinor, if set (even when below cost)
    //   otherwise nothing, if the cost is not known
    //   otherwise cost plus the shop's margin, rounded up to 10 KES
    //
    // A cost of zero counts as not known: it is what Salio reports for a part
    // that has never been received, not a real buying price. No cost means no
    // stop, never a guess.
    public static long? StopPrice(long? minPriceMinor, long? unitCostMinor, int minMarginPercent)
    {
        if (minPriceMinor is not null)
        {
            return minPriceMinor;
        }

        if (unitCostMinor is null || unitCostMinor <= 0)
        {
            return null;
        }

        // cost * (100 + margin) / 100, then up to the next 10 KES, in one
        // division so nothing is cut off before the rounding.
        long scaled = unitCostMinor.Value * (100 + minMarginPercent);
        long per = 100 * RoundToMinor;
        long steps = (scaled + per - 1) / per;

        return steps * RoundToMinor;
    }
}
