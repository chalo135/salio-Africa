using System.ComponentModel.DataAnnotations;

namespace Salio.Api.Dtos;

public class UpdateSellableNounRequest
{
    // Optional: the server resolves the shop (locked rule 9).
    public Guid? OrganizationId { get; set; }

    // What the shop calls the things it sells, plural: "Parts", "Services".
    [Required]
    [MaxLength(30)]
    public string SellableNoun { get; set; } = string.Empty;
}
