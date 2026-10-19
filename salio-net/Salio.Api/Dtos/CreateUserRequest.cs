using System.ComponentModel.DataAnnotations;
using Salio.Domain.Enums;

namespace Salio.Api.Dtos;

public class CreateUserRequest
{
    // Optional: with one shop the server knows which it is. If it is sent,
    // the server still checks the caller belongs to it (locked rule 9).
    public Guid? OrganizationId { get; set; }

    [Required]
    [MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    // Checked for length by UserCreator, then hashed. Never stored as typed.
    [Required]
    public string Password { get; set; } = string.Empty;

    [Required]
    public MembershipRole Role { get; set; }
}
