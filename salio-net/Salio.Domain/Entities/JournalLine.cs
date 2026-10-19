namespace Salio.Domain.Entities
{
    public class JournalLine
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public Guid JournalEntryId { get; set; }
        public Guid AccountId { get; set; }
        public long DebitMinor { get; set; }
        public long CreditMinor { get; set; }
        public string Currency { get; set; } = "KES";

        // Which customer this line belongs to. Set only on lines posted to
        // 1100 Accounts Receivable, so a customer's balance is the sum of
        // their lines there, worked out on read (locked rule 6). Null on
        // every other line.
        public Guid? CustomerId { get; set; }
    }
}
