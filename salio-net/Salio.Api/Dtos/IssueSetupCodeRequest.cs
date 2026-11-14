using System.ComponentModel.DataAnnotations;

namespace Salio.Api.Dtos;

// What the admin types to issue a setup code: who it is for.
public class IssueSetupCodeRequest
{
    [Required]
    [MaxLength(200)]
    public string ShopName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;
}
