namespace Salio.Domain.Entities
{
    public class SaleLine
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public Guid SaleId { get; set; }
        public Guid ProductId { get; set; }
        public decimal Quantity { get; set; }
        public long UnitPriceMinor { get; set; }
        public long LineTotalMinor { get; set; }

        public char TaxBand { get; set; }
        public long UnitCostMinor { get; set; }

    }
}
