namespace Salio.Domain.Entities
{
    public class SaleLine
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public Guid SaleId { get; set; }
        public Guid ProductId { get; set; }

        // The part's name and SKU as they were when it was sold. A receipt
        // is a record: renaming a part later must not change what an old
        // receipt says, so these are copied here, never read live.
        public string ProductName { get; set; } = string.Empty;
        public string Sku { get; set; } = string.Empty;

        public decimal Quantity { get; set; }
        public long UnitPriceMinor { get; set; }

        // The part's asking price when it was sold, read from the part by
        // the server. UnitPriceMinor above is what was actually charged.
        // Null on lines sold before this was recorded.
        public long? ListPriceMinor { get; set; }
        public long LineTotalMinor { get; set; }

        public char TaxBand { get; set; }
        public long UnitCostMinor { get; set; }

    }
}
