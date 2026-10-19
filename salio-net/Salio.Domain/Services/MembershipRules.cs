namespace Salio.Domain.Services;

using Salio.Domain.Entities;
using Salio.Domain.Enums;

/// <summary>
/// Locked rule 9, in one place and with no database in sight, so it can be
/// tested without one. The caller loads the user's memberships; this decides
/// whether the request may go ahead.
///
/// An organisation id from the client is only ever checked against the
/// user's own memberships. It is never used as given.
/// </summary>
public class MembershipRules
{
    public MembershipAccessResult Resolve(
        List<Membership> memberships,
        Guid? requestedOrganizationId)
    {
        if (memberships.Count == 0)
        {
            return Outcome(MembershipAccessOutcome.NoMemberships);
        }

        if (requestedOrganizationId is null)
        {
            // Nothing named. Deciding for the user is only safe when there is
            // exactly one shop it could possibly be.
            if (memberships.Count == 1)
            {
                return Allowed(memberships[0]);
            }

            return Outcome(MembershipAccessOutcome.MustChooseOrganization);
        }

        foreach (var membership in memberships)
        {
            if (membership.OrganizationId == requestedOrganizationId.Value)
            {
                return Allowed(membership);
            }
        }

        // Named a shop they have no row for. Refused whether or not that shop
        // exists, so guessing ids tells the caller nothing.
        return Outcome(MembershipAccessOutcome.NotAMember);
    }

    private static MembershipAccessResult Allowed(Membership membership) => new()
    {
        Outcome = MembershipAccessOutcome.Allowed,
        OrganizationId = membership.OrganizationId,
        Role = membership.Role
    };

    private static MembershipAccessResult Outcome(MembershipAccessOutcome outcome) => new()
    {
        Outcome = outcome
    };
}
