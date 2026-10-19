namespace Salio.Domain.Services;

using Salio.Domain.Enums;

// The answer to "may this user touch this organisation, and as what?".
// OrganizationId and Role mean nothing unless Outcome is Allowed.
public class MembershipAccessResult
{
    public MembershipAccessOutcome Outcome { get; set; }
    public Guid OrganizationId { get; set; }
    public MembershipRole Role { get; set; }

    public bool IsAllowed => Outcome == MembershipAccessOutcome.Allowed;
    public bool IsOwner => IsAllowed && Role == MembershipRole.Owner;
}
