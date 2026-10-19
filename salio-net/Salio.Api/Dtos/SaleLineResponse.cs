namespace Salio.Api.Dtos;

// One line of a receipt. Selling price only: what a part cost the shop is
// the Owner's business and lives in the reports (locked rule 13).
public class SaleLineResponse
{
    public string ProductName { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public long UnitPriceMinor { get; set; }
}
