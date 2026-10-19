namespace Salio.Domain.Enums
{
    public enum MembershipAccessOutcome
    {
        // The user is a member of the organisation and may proceed.
        Allowed,

        // The user belongs to no organisation at all.
        NoMemberships,

        // The user named an organisation they do not belong to.
        NotAMember,

        // The user named nothing and belongs to several, so the server
        // cannot decide for them.
        MustChooseOrganization
    }
}
