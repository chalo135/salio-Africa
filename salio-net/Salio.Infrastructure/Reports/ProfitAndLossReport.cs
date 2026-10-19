namespace Salio.Infrastructure.Reports
{
    public class ProfitAndLossReport
    {
        public DateOnly From { get; set; }
        public DateOnly To { get; set; }

        public List<ProfitAndLossRow> RevenueRows { get; set; } = new();

        // Account 5000 only: what the goods sold cost the shop.
        public List<ProfitAndLossRow> CostOfSalesRows { get; set; } = new();

        // Every other expense account, one row each: rent, power, wages, and
        // any category the owner has added. Never Drawings, which is equity.
        public List<ProfitAndLossRow> RunningCostRows { get; set; } = new();

        public long RevenueTotalMinor { get; set; }
        public long CostOfSalesTotalMinor { get; set; }
        public long GrossProfitMinor { get; set; }

        public long RunningCostsTotalMinor { get; set; }
        public long NetProfitMinor { get; set; }
    }
}
