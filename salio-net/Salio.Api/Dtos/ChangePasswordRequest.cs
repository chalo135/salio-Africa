using System.ComponentModel.DataAnnotations;

namespace Salio.Api.Dtos;

public class ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    // At least 8 characters. No other rule.
    [Required]
    public string NewPassword { get; set; } = string.Empty;
}
