using Salio.Domain.Enums;

namespace Salio.Domain.Entities
{
    // The shopkeeper's record that she filed a return with KRA herself.
    // Salio never files anything. Once this row exists, the books for
    // PeriodFrom to PeriodTo are closed: JournalPoster refuses new entries
    // dated inside it, so the filed figure can never silently change.
    public class FiledReturn
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public DateOnly PeriodFrom { get; set; }
        public DateOnly PeriodTo { get; set; }
        public TaxType TaxType { get; set; }
        public long AmountMinor { get; set; }
        public DateTimeOffset FiledAt { get; set; }

        // The acknowledgement number iTax shows after she submits.
        public string AcknowledgementReference { get; set; } = string.Empty;
    }
}
