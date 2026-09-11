namespace Salio.Infrastructure.Reports
{
    public class TrialBalanceReport
    {
        public DateOnly AsOf { get; set; }
        public List<TrialBalanceRow> Rows { get; set; } = new();

        // The grand total. If the books are sound these two are equal.
        public long TotalDebitMinor { get; set; }
        public long TotalCreditMinor { get; set; }
    }
}
