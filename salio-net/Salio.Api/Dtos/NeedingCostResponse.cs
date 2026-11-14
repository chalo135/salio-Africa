namespace Salio.Api.Dtos;

// One part whose average cost is zero while it has sold or has stock
// (decision D9). Until it gets a cost, its sales show no cost of sales and
// the profit figure is too high.
public class NeedingCostResponse
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;

    // Every unit sold, in finished sales.
    public decimal QuantitySold { get; set; }

    // Sales booked with a unit cost of zero: no cost of sales at all.
    public long RevenueWithoutCostMinor { get; set; }

    public decimal QuantityOnHand { get; set; }
}

public class NeedingCostCountResponse
{
    public int Count { get; set; }
}
