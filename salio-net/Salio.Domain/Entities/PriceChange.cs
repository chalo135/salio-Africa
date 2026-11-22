namespace Salio.Domain.Entities
{
    // One change to a part's selling price: what it was, what it became,
    // who did it and when. Written by PriceChanges.Record wherever a selling
    // price is set. Never updated, never deleted: history cannot be rebuilt
    // once lost.
    public class PriceChange
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public Guid ProductId { get; set; }

        // Null when the part was created with this price: there was none.
        public long? OldPriceMinor { get; set; }
        public long NewPriceMinor { get; set; }

        // Null only when no person did it (the demo seeder).
        public Guid? ChangedByUserId { get; set; }
        public DateTimeOffset ChangedAt { get; set; }
    }
}
