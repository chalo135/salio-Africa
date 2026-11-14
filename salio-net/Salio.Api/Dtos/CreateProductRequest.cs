using System.ComponentModel.DataAnnotations;

namespace Salio.Api.Dtos;

// Id and IsActive are not here on purpose: the server decides those.
public class CreateProductRequest
{
    // Optional. With one shop the server knows which it is; if it is sent,
    // the server still checks the caller belongs to it (locked rule 9).
    public Guid? OrganizationId { get; set; }

    // Lengths match ProductConfiguration, so a long value gets a 400, not a database error.
    [Required(ErrorMessage = "A part needs a SKU")]
    [MaxLength(50, ErrorMessage = "A SKU can be at most 50 characters")]
    public string Sku { get; set; } = string.Empty;

    [Required(ErrorMessage = "A part needs a name")]
    [MaxLength(200, ErrorMessage = "A name can be at most 200 characters")]
    public string Name { get; set; } = string.Empty;

    [Range(0, long.MaxValue, ErrorMessage = "The selling price cannot be below zero")]
    public long SellPriceMinor { get; set; }

    // Where it sits on the shelf. Optional: not every item has a fixed place.
    [MaxLength(20, ErrorMessage = "A shelf code can be at most 20 characters")]
    public string? BinLocation { get; set; }
}
