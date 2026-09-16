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

    // Derived from StockMovements every time. Never stored.
    public decimal QuantityOnHand { get; set; }
}
