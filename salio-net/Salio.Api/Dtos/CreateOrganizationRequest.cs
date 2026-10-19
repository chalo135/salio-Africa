using System.ComponentModel.DataAnnotations;

namespace Salio.Api.Dtos;

public class CreateOrganizationRequest
{
    [Required]
    [MaxLength(200)]
    public string ShopName { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string OwnerFullName { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string OwnerEmail { get; set; } = string.Empty;

    [Required]
    public string OwnerPassword { get; set; } = string.Empty;
}
