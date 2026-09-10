namespace Salio.Domain.Entities
{
    public class Product
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public string Sku { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long SellPriceMinor { get; set; }
        public bool IsActive { get; set; } = true;

        // eTIMS — nullable and unused until KRA integration is built.
        public string? EtimsItemClsCd { get; set; }
        public string? EtimsPkgUnitCd { get; set; }
        public string? EtimsQtyUnitCd { get; set; }
        public string? EtimsOriginCountry { get; set; }
    }
}
