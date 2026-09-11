using Salio.Domain.Enums;

namespace Salio.Infrastructure.Reports
{
    // One account's contribution over the period. AmountMinor is already
    // signed the way its class reads: income as credits minus debits,
    // expense as debits minus credits.
    public class ProfitAndLossRow
    {
        public Guid AccountId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public AccountClass AccountClass { get; set; }
        public long AmountMinor { get; set; }
    }
}
