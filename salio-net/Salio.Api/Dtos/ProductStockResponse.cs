namespace Salio.Api.Dtos;

// One line of the shelf list: what it is, what it costs, where it sits,
// and how many are there.
public class ProductStockResponse
{
    public Guid Id { get; set; }

    public string Sku { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public long SellPriceMinor { get; set; }

    public string? BinLocation { get; set; }

    // Filled in by a product import or by hand. All optional.
    public string? Category { get; set; }
    public string? Barcode { get; set; }
    public int? ReorderLevel { get; set; }

    // False for a retired part. Only the Parts screen ever sees one: the
    // till's shelf list leaves them out.
    public bool IsActive { get; set; }

    // Both derived from StockMovements every time. Never stored.
    public decimal QuantityOnHand { get; set; }

    // True when QuantityOnHand is below zero: more has been sold than Salio
    // was told arrived. A prompt to receive stock, never a reason to refuse
    // a sale (locked rule 16).
    public bool NeedsReceiving { get; set; }

    // What one unit currently costs the shop, weighted across deliveries.
    // Null for a Cashier: locked rule 13 keeps cost and stock value for the
    // Owner, and average cost is what stock value is made of.
    // Also null while NeedsReceiving is true: the cost is unknown until stock
    // is received, so no figure is given (locked rule 16).
    public long? AverageCostMinor { get; set; }
}
