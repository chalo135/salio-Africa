using Salio.Domain.Enums;

namespace Salio.Api.Dtos;

// One shop this person belongs to, and what they may do there.
public class ShopMembershipResponse
{
    public Guid OrganizationId { get; set; }

    public string Name { get; set; } = string.Empty;

    public MembershipRole Role { get; set; }

    // What this shop calls its things: "Parts", "Products". The till uses it
    // in place of "Parts". The shop's SellableNoun, kept under this name so
    // existing screens keep working.
    public string ItemWord { get; set; } = "Parts";

    // "Sales" or "Services": which screens come first. Never hides stock.
    public string BusinessKind { get; set; } = "Sales";

    // The trade chosen at setup, as the option read.
    public string Trade { get; set; } = string.Empty;
}
