namespace Salio.Infrastructure.Reports
{
    public class DaySheetReport
    {
        public DateOnly Date { get; set; }
        public int SaleCount { get; set; }
        public long GrossSalesMinor { get; set; }

        public List<PaymentMethodTotal> PaymentTotals { get; set; } = new();

        // Cash taken at the till on this date. It does not include an opening
        // float: a day is not a shift, and only a shift has one.
        public long ExpectedCashMinor { get; set; }

        public List<TopProductRow> TopProducts { get; set; } = new();
    }
}
