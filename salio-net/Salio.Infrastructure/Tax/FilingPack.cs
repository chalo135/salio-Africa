namespace Salio.Infrastructure.Tax
{
    // Everything the shopkeeper needs to file Turnover Tax herself. If
    // Exceptions is not empty, the figure is not safe to file yet.
    public class FilingPack
    {
        public DateOnly From { get; set; }
        public DateOnly To { get; set; }

        // The headline: what she types into iTax.
        public long TaxDueMinor { get; set; }
        public long GrossTurnoverMinor { get; set; }

        public decimal RatePercent { get; set; }
        public DateOnly RateEffectiveFrom { get; set; }

        public int SaleCount { get; set; }

        // Plain English, one problem per line, each one something she can fix.
        public List<string> Exceptions { get; set; } = new();
    }
}
