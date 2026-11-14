using System.ComponentModel.DataAnnotations;

namespace Salio.Api.Dtos;

public class CreateStockCountLineRequest
{
    [Required]
    public Guid ProductId { get; set; }

    // How many were actually on the shelf. Zero is allowed: the shelf was empty.
    public decimal CountedQuantity { get; set; }
}
