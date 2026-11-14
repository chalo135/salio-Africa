using System.ComponentModel.DataAnnotations;

namespace Salio.Api.Dtos;

public class PinRequest
{
    [Required(ErrorMessage = "Type your 4-digit PIN")]
    [MaxLength(10)]
    public string Pin { get; set; } = string.Empty;
}
