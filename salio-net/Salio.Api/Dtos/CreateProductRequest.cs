using System.ComponentModel.DataAnnotations;

namespace Salio.Api.Dtos;

// Id and IsActive are not here on purpose: the server decides those.
public class CreateProductRequest
{
    // TODO: must come from the authenticated user once authentication exists.
    [Required]
    public Guid OrganizationId { get; set; }

    // Lengths match ProductConfiguration, so a long value gets a 400, not a database error.
    [Required]
    [MaxLength(50)]
    public string Sku { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Range(0, long.MaxValue)]
    public long SellPriceMinor { get; set; }
}
