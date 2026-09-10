namespace Salio.Domain.Entities
{
    // Rates are data, never constants in code. Kenyan rates are disputed
    // between sources and change with each Finance Act, so every rate carries
    // the dates it applies to and where it came from.
    public class TaxRate
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public char Band { get; set; }
        public decimal RatePercent { get; set; }
        public DateOnly EffectiveFrom { get; set; }
        public DateOnly? EffectiveTo { get; set; }
        public string Source { get; set; } = string.Empty;
    }
}
