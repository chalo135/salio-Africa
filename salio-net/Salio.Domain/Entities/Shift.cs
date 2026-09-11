using Salio.Domain.Enums;

namespace Salio.Domain.Entities
{
    // One person's turn at the till, from opening the drawer to counting it.
    // The closing fields stay null until the shift is actually closed.
    public class Shift
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public string OpenedBy { get; set; } = string.Empty;
        public DateTimeOffset OpenedAt { get; set; }
        public long OpeningFloatMinor { get; set; }
        public DateTimeOffset? ClosedAt { get; set; }
        public long? CountedCashMinor { get; set; }
        public ShiftStatus Status { get; set; }
    }
}
