using System.ComponentModel.DataAnnotations;

namespace Salio.Api.Dtos;

// Id and IsActive are not here on purpose: the server decides those.
public class CreateProductRequest
{
    // Optional. With one shop the server knows which it is; if it is sent,
    // the server still checks the caller belongs to it (locked rule 9).
    public Guid? OrganizationId { get; set; }

    // Lengths match ProductConfiguration, so a long value gets a 400, not a database error.
    [Required]
    [MaxLength(50)]
    public string Sku { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Range(0, long.MaxValue)]
    public long SellPriceMinor { get; set; }

    // Where it sits on the shelf. Optional: not every item has a fixed place.
    [MaxLength(20)]
    public string? BinLocation { get; set; }
}
