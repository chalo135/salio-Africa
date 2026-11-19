namespace Salio.Domain.Entities
{
    public class Product
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public string Sku { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long SellPriceMinor { get; set; }

        // The owner's own lowest price for this part, when set. Overrides
        // the stop price worked out from cost (PricingRules.StopPrice).
        public long? MinPriceMinor { get; set; }
        public bool IsActive { get; set; } = true;

        // Where the item physically sits, so it can be found without asking
        // someone who remembers. Nullable: stock is stored wherever it fits.
        public string? BinLocation { get; set; }

        // Filled in by a product import or by hand. All optional.
        public string? Category { get; set; }
        public string? Barcode { get; set; }

        // When stock falls to this many, it is time to order more.
        public int? ReorderLevel { get; set; }

        // eTIMS — nullable and unused until KRA integration is built.
        public string? EtimsItemClsCd { get; set; }
        public string? EtimsPkgUnitCd { get; set; }
        public string? EtimsQtyUnitCd { get; set; }
        public string? EtimsOriginCountry { get; set; }
    }
}
