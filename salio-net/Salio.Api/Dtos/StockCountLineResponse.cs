namespace Salio.Api.Dtos;

public class StockCountLineResponse
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = "";

    // What the stock movements said was on hand before the count.
    public decimal ExpectedQuantity { get; set; }

    public decimal CountedQuantity { get; set; }

    // Counted minus expected: below zero is missing, above zero is found,
    // zero is a match and nothing was written for it.
    public decimal Difference { get; set; }

    // The weighted average cost the difference was valued at.
    public long UnitCostMinor { get; set; }

    // The difference's value, rounded once to whole minor units. Always
    // zero or more; Difference says which way it went.
    public long ValueMinor { get; set; }
}
