using System.ComponentModel.DataAnnotations;

namespace Salio.Api.Dtos;

public class CreateStockReceiptLineRequest
{
    [Required]
    public Guid ProductId { get; set; }

    public decimal Quantity { get; set; }

    // What ONE unit cost the shop, in minor units.
    public long UnitCostMinor { get; set; }
}
