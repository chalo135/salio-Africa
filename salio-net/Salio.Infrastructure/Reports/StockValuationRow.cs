namespace Salio.Infrastructure.Reports
{
    public class StockValuationRow
    {
        public Guid ProductId { get; set; }
        public string Sku { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public long UnitCostMinor { get; set; }
        public long ValueMinor { get; set; }
    }
}
