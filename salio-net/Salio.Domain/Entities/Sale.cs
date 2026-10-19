using Salio.Domain.Enums;

namespace Salio.Domain.Entities
{
    public class Sale
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public string SaleNumber { get; set; } = string.Empty;
        public DateOnly SaleDate { get; set; }
        public DateTimeOffset OccurredAt { get; set; }
        public long TotalMinor { get; set; }
        public SaleStatus Status { get; set; }

        // Which turn at the till this sale belongs to. Nullable because sales
        // already in the database were recorded before shifts existed.
        public Guid? ShiftId { get; set; }

        // Who sold it. Nullable for the same reason as ShiftId: sales made
        // before logins existed have nobody to name, and guessing would put
        // a false fact in the books. Locked rule 13 needs this so a cashier
        // can be shown their own sales.
        public Guid? AppUserId { get; set; }

        // Who took the goods on credit. Null for a cash or M-Pesa sale. The
        // debt itself lives on the journal line, not here.
        public Guid? CustomerId { get; set; }

        // eTIMS — nullable and unused until KRA integration is built.
        public string? EtimsControlNumber { get; set; }
        public string? EtimsSignature { get; set; }
        public string? EtimsQr { get; set; }
        public EtimsStatus EtimsStatus { get; set; } = EtimsStatus.NotRequired;
    }
}
