using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Salio.Domain.Enums;

namespace Salio.Api.Dtos;

// Everything a stranger answers to set up their own shop.
public class SetUpShopRequest
{
    [Required]
    [MaxLength(64)]
    public string SetupCode { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string ShopName { get; set; } = string.Empty;

    // "Sales" or "Services". Sets wording and screen order only; it never
    // switches stock off.
    [Required]
    [MaxLength(20)]
    public string BusinessKind { get; set; } = string.Empty;

    // The trade option the client tapped, or what they typed for
    // "Something else".
    [Required]
    [MaxLength(120)]
    public string Trade { get; set; } = string.Empty;

    // Worked out from the trade by the wizard; the client is never asked.
    [Required]
    [MaxLength(30)]
    public string SellableNoun { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string OwnerFullName { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string OwnerEmail { get; set; } = string.Empty;

    // Typed by the owner, hashed by PasswordService, never stored or shown.
    [Required]
    public string OwnerPassword { get; set; } = string.Empty;

    // "TurnoverTax", "VatRegistered" or "NotSure". Name or number.
    [Required]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TaxPosition TaxPosition { get; set; }
}
