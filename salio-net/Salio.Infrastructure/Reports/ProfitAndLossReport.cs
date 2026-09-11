namespace Salio.Infrastructure.Reports
{
    public class ProfitAndLossReport
    {
        public DateOnly From { get; set; }
        public DateOnly To { get; set; }

        public List<ProfitAndLossRow> RevenueRows { get; set; } = new();
        public List<ProfitAndLossRow> CostOfSalesRows { get; set; } = new();

        public long RevenueTotalMinor { get; set; }
        public long CostOfSalesTotalMinor { get; set; }
        public long GrossProfitMinor { get; set; }
    }
}
