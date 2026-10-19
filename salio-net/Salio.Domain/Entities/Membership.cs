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
    }
}
