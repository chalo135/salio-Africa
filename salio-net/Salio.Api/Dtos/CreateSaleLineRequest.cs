using System.ComponentModel.DataAnnotations;

namespace Salio.Api.Dtos;

public class CreateSaleLineRequest
{
    [Required]
    public Guid ProductId { get; set; }

    public decimal Quantity { get; set; }

    public long UnitPriceMinor { get; set; }

    // Kenyan VAT band, 'A' to 'E'.
    public char TaxBand { get; set; }
}
