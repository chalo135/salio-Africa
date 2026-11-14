using System.ComponentModel.DataAnnotations;
using Salio.Domain.Enums;

namespace Salio.Api.Dtos;

// No password: Salio makes a temporary one and the person replaces it.
public class CreatePersonRequest
{
    public Guid? OrganizationId { get; set; }

    [Required]
    [MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public MembershipRole Role { get; set; }
}
