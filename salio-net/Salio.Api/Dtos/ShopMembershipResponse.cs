using Salio.Domain.Enums;

namespace Salio.Api.Dtos;

// One shop this person belongs to, and what they may do there.
public class ShopMembershipResponse
{
    public Guid OrganizationId { get; set; }

    public string Name { get; set; } = string.Empty;

    public MembershipRole Role { get; set; }
}
