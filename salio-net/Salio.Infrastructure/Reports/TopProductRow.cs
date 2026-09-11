namespace Salio.Infrastructure.Reports
{
    public class TopProductRow
    {
        public Guid ProductId { get; set; }
        public string Sku { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal QuantitySold { get; set; }
    }
}
