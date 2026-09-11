namespace Salio.Infrastructure.Tax
{
    // The figure, and enough about the rate behind it that the shopkeeper
    // can check it against KRA before she files.
    public class TurnoverTaxResult
    {
        public long GrossTurnoverMinor { get; set; }
        public decimal RatePercent { get; set; }
        public DateOnly RateEffectiveFrom { get; set; }
        public string RateSource { get; set; } = string.Empty;
        public long TaxDueMinor { get; set; }
    }
}
