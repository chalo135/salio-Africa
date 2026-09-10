using Salio.Domain.Enums;

namespace Salio.Domain.Entities
{
    // Append-only. Never update or delete a row here; correct a mistake by
    // writing a new movement in the opposite direction.
    public class StockMovement
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public Guid ProductId { get; set; }
        public StockMovementType Type { get; set; }
        public decimal QuantityChange { get; set; }
        public long UnitCostMinor { get; set; }
        public DateTimeOffset OccurredAt { get; set; }
        public string? Note { get; set; }
    }
}
