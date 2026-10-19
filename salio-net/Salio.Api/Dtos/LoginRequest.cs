using System.ComponentModel.DataAnnotations;

namespace Salio.Api.Dtos;

public class LoginRequest
{
    [Required]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
