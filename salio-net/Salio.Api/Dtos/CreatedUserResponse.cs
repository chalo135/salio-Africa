using Salio.Domain.Enums;

namespace Salio.Api.Dtos;

// Never carries a password or a hash.
public class CreatedUserResponse
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public MembershipRole Role { get; set; }

    public Guid OrganizationId { get; set; }
}
