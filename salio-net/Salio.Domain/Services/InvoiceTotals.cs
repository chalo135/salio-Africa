namespace Salio.Domain.Services;

// One invoice line as the totals check sees it.
public class InvoiceTotalsLine
{
    public decimal Quantity { get; set; }
    public long UnitCostMinor { get; set; }
    public bool Accepted { get; set; }
}

public class InvoiceTotalsResult
{
    // The ticked lines added up: what the stock receipt would cost.
    public long LinesTotalMinor { get; set; }

    // True only when the invoice's own total was read AND equals the lines.
    public bool TotalsAgree { get; set; }

    // Invoice total minus the lines. 0 when the invoice total could not be
    // read; the screen tells that case apart by the total being null.
    public long DifferenceMinor { get; set; }
}

// The arithmetic check: does the total printed on the invoice equal the sum
// of the lines? It only reports; it never changes a line.
public static class InvoiceTotals
{
    public static InvoiceTotalsResult Check(long? extractedTotalMinor, List<InvoiceTotalsLine> lines)
    {
        long linesTotal = 0;

        foreach (var line in lines)
        {
            if (!line.Accepted)
            {
                continue;
            }

            linesTotal = linesTotal + LineTotal(line.Quantity, line.UnitCostMinor);
        }

        if (extractedTotalMinor is null)
        {
            return new InvoiceTotalsResult
            {
                LinesTotalMinor = linesTotal,
                TotalsAgree = false,
                DifferenceMinor = 0
            };
        }

        long difference = extractedTotalMinor.Value - linesTotal;

        return new InvoiceTotalsResult
        {
            LinesTotalMinor = linesTotal,
            TotalsAgree = difference == 0,
            DifferenceMinor = difference
        };
    }

    // One line's cost, rounded exactly as StockReceiptService.TotalCost
    // rounds a receipt line (away from zero), so the figure shown here is the
    // figure the stock receipt will book. A test holds the two together.
    public static long LineTotal(decimal quantity, long unitCostMinor)
    {
        return (long)Math.Round(quantity * unitCostMinor, MidpointRounding.AwayFromZero);
    }
}
