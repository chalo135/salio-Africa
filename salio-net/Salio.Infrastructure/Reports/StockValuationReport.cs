namespace Salio.Infrastructure.Reports
{
    public class StockValuationReport
    {
        public DateOnly AsOf { get; set; }
        public List<StockValuationRow> Rows { get; set; } = new();
        public long TotalValueMinor { get; set; }
    }
}
