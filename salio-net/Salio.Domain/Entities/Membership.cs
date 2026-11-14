using Salio.Domain.Enums;

namespace Salio.Domain.Entities
{
    // One person's place in one organisation, and what they may do there
    // (locked rule 10). No row for an organisation means the user can see
    // nothing of it at all.
    //
    // A person working at two shops has two rows here, one per shop, and can
    // hold a different role in each.
    public class Membership
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public Guid AppUserId { get; set; }
        public MembershipRole Role { get; set; }
        public DateTimeOffset CreatedAt { get; set; }

        // Set when an Owner switches this person off in this shop. Null means
        // active. Per shop, not per person: someone who also works at another
        // shop keeps that one (locked rule 10). The row is never deleted,
        // because their sales stay in the books.
        //
        // A date rather than a bool, so the column could be added to rows
        // that already exist without a default that would switch them all
        // off.
        public DateTimeOffset? DeactivatedAt { get; set; }
    }
}
