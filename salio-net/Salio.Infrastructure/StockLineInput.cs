namespace Salio.Infrastructure;

// One line handed to OpeningStockWriter or StockReceiptWriter: which part,
// how many, and what ONE unit cost the shop, in minor units. The caller has
// already decided who may do this; the writers check everything else.
public class StockLineInput
{
    public Guid ProductId { get; set; }
    public decimal Quantity { get; set; }
    public long UnitCostMinor { get; set; }
}
