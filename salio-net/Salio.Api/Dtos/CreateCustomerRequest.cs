using System.ComponentModel.DataAnnotations;

namespace Salio.Api.Dtos;

public class CreateCustomerRequest
{
    public Guid? OrganizationId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(30)]
    public string Phone { get; set; } = string.Empty;
}
