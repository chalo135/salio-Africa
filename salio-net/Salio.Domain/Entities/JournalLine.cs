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
    }
}
