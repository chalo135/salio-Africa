namespace Salio.Api.Dtos;

// A part added at the counter when a search finds nothing (decision D8).
// No SKU: the server makes one. Cost and quantity are Owner matters; from a
// Cashier they are ignored (locked rule 13).
public class QuickAddProductRequest
{
    public string? Name { get; set; }

    public long? SellPriceMinor { get; set; }

    // Optional. Leave empty if the cost is not known yet: the part is still
    // created and still sells.
    public long? UnitCostMinor { get; set; }

    // Optional. Needs a unit cost above zero; posted as opening stock. Only
    // before the shop's first sale: after it, sending a quantity or a cost
    // is refused (see GET quick-add/rules).
    public decimal? Quantity { get; set; }
}
