using System.ComponentModel.DataAnnotations;

namespace Salio.Api.Dtos;

public class CreateOpeningStockLineRequest
{
    [Required]
    public Guid ProductId { get; set; }

    // How many are on the shelf.
    public decimal Quantity { get; set; }

    // What ONE unit cost the shop when it was bought, in minor units.
    public long UnitCostMinor { get; set; }
}
