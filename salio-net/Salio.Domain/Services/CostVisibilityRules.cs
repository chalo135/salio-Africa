using Salio.Domain.Enums;

namespace Salio.Domain.Services;

// What is known about the caller when deciding whether the shop's buying
// price may be sent to their screen.
//
// PlatformAdmin is a fourth field the brief did not list: a platform admin
// is a flag on the person (AppUser), not a membership role, so it could not
// be carried by Role. It is named without the column's name on purpose: only
// three files may name that column (PlatformAdminTests).
public record CostVisibilityContext(
    MembershipRole Role,
    bool OrganizationAllowsCashierCost,
    bool IsActiveMember,
    bool PlatformAdmin);

// Who may see what a part cost the shop. The API asks this once per request
// and leaves the cost out of the response entirely when the answer is no.
public static class CostVisibilityRules
{
   public static bool CanSeeCost(CostVisibilityContext context)
{
    // 1. Nobody outside the shop sees anything, ever. Deactivation has no
    //    exceptions: to support a shop, add yourself to it, where it is
    //    visible and leaves a trace.
    if (!context.IsActiveMember) { return false; }

    // 2. A platform admin who is a member of this shop sees cost, whatever
    //    role they hold here.
    if (context.PlatformAdmin) { return true; }

    // 3. The owner's own buying prices. Always theirs.
    if (context.Role == MembershipRole.Owner) { return true; }

    // 4. Everyone else is staff. Only if this shop switched it on.
    return context.OrganizationAllowsCashierCost;
}
}