using Salio.Domain.Enums;

namespace Salio.Domain.Entities
{
    // One tender against a sale. A sale can have several of these: paying
    // 1000 in cash and the rest by M-Pesa is two Payment rows, not one.
    public class Payment
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public Guid SaleId { get; set; }
        public PaymentMethod Method { get; set; }
        public long AmountMinor { get; set; }

        // The M-Pesa confirmation code, typed in by the person at the till.
        // Null for cash, where there is nothing to record.
        public string? Reference { get; set; }

        public DateTimeOffset ReceivedAt { get; set; }
    }
}
