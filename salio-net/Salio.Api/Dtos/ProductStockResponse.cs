using System.Text.Json.Serialization;

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

    // The lowest price this part should go for: the owner's own minimum, or
    // cost plus the shop's margin (PricingRules.StopPrice). Null when the
    // cost is not known. Sent whatever the caller may see of the cost.
    public long? StopPriceMinor { get; set; }

    // Whether CostVisibilityRules let this caller see the buying price. Lets
    // the screen tell "hidden" apart from "not known yet".
    public bool BuyingPriceShown { get; set; }

    // What one unit currently costs the shop, weighted across deliveries.
    // Left out of the JSON entirely, not sent as null, when the caller may
    // not see it (CostVisibilityRules). Also left out while NeedsReceiving is
    // true: the cost is unknown until stock is received (locked rule 16).
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? AverageCostMinor { get; set; }

    // False when the cost came from a product import, or no person ever
    // stated one (CostProvenanceRules). Such a cost gives no stop price.
    // Left out, like the cost itself, when the caller may not see the cost.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CostIsConfirmed { get; set; }

    // What a person last paid for one, on the newest stock in, supplier
    // invoice or opening stock (CostProvenanceRules.LastBoughtAtMinor).
    // Survives selling out. Guidance only, never posted. Left out, like the
    // cost, when the caller may not see the cost, and when there is none.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? LastBoughtAtMinor { get; set; }

    // The shop's margin rule, in percent, when this part's last bought price
    // plus that margin is more than its asking price
    // (PricingRules.MarginUnderRule). For the Inventory screen's note, never
    // the till's. Left out otherwise, and always when the caller may not
    // see the cost.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MarginUnderRulePercent { get; set; }
}
