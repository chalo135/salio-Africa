using Salio.Domain.Enums;

namespace Salio.Infrastructure.Reports
{
    // One line on 1100 Accounts Receivable for one customer: a credit sale
    // (a debit, they owe more) or a payment (a credit, they owe less).
    public class CustomerLedgerRow
    {
        public Guid JournalEntryId { get; set; }
        public DateOnly EntryDate { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public SourceType SourceType { get; set; }
        public Guid? SourceId { get; set; }
        public long DebitMinor { get; set; }
        public long CreditMinor { get; set; }
    }
}
