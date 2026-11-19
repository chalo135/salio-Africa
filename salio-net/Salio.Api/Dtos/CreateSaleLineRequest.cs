using System.ComponentModel.DataAnnotations;

namespace Salio.Api.Dtos;

public class CreateSaleLineRequest
{
    [Required]
    public Guid ProductId { get; set; }

    public decimal Quantity { get; set; }

    // The price agreed at the counter, per unit. Never above the part's own
    // asking price, which the server reads from the part.
    public long UnitPriceMinor { get; set; }

    // Kenyan VAT band, 'A' to 'E'.
    public char TaxBand { get; set; }
}
