using Salio.Domain.Enums;

namespace Salio.Infrastructure.Reports
{
    // One account's totals. Nothing here is stored: it is summed from journal
    // lines every time the report is asked for.
    public class TrialBalanceRow
    {
        public Guid AccountId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public AccountClass AccountClass { get; set; }
        public long TotalDebitMinor { get; set; }
        public long TotalCreditMinor { get; set; }
    }
}
