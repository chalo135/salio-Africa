using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Salio.Api.Dtos;

// What an Owner may change about a part. Every field is sent every time.
// Quantity and cost are not here: they come from stock movements and are
// never typed. Which shop comes from ?organizationId=, never the body.
public class UpdateProductRequest
{
    // Lengths match ProductConfiguration, so a long value gets a 400, not a database error.
    // [Required] also refuses a value that is only spaces.
    [Required(ErrorMessage = "A part needs a SKU")]
    [MaxLength(50, ErrorMessage = "A SKU can be at most 50 characters")]
    public string Sku { get; set; } = string.Empty;

    [Required(ErrorMessage = "A part needs a name")]
    [MaxLength(200, ErrorMessage = "A name can be at most 200 characters")]
    public string Name { get; set; } = string.Empty;

    // Nullable so a missing price is refused instead of silently becoming 0.
    // Zero itself is allowed: a shop sometimes gives a part away.
    [Required(ErrorMessage = "Send the selling price, even if it has not changed")]
    [Range(0, long.MaxValue, ErrorMessage = "The selling price cannot be below zero")]
    public long? SellPriceMinor { get; set; }

    [MaxLength(20, ErrorMessage = "A shelf code can be at most 20 characters")]
    public string? BinLocation { get; set; }

    // Anything else the caller sent lands here, so the controller can refuse
    // it by name rather than ignore it. Someone sending a quantity must be
    // told it did nothing.
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unexpected { get; set; }
}
