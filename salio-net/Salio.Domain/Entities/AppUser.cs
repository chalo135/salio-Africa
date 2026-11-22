namespace Salio.Domain.Entities
{
    // A person who can log in. One row per human, not per shop.
    //
    // This entity has no OrganizationId, and so is exempt from locked rule 2.
    // (So are TrustedDevice, a person's phone; SetupCode, which exists before
    // its shop; AdminAuditLog, the platform admin's own record; and
    // Organization, the shop itself.) It is exempt because of locked
    // rule 10: a user may belong to more than one organisation, so which
    // shops they can see is held in Membership, not here. Putting an
    // OrganizationId on this row would make a second job impossible and
    // force a duplicate person, with a second password, for every shop.
    public class AppUser
    {
        public Guid Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        // Only ever a hash from ASP.NET Core Identity's password hasher
        // (locked rule 11). No password is stored, and nothing in this repo
        // hashes anything by hand.
        public string PasswordHash { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; set; }

        // Changes whenever every device holding this person's login must be
        // signed out (locked rule 12). The value travels in the cookie and is
        // compared on each request: a new value here makes old cookies dead,
        // and nobody's password changes.
        public Guid SecurityStamp { get; set; }

        // A person who has left. Their sales stay in the books, so the row is
        // never deleted. This switches off the whole account, in every shop;
        // leaving one shop is Membership.DeactivatedAt instead.
        public bool IsActive { get; set; } = true;

        // True for a person given a temporary password by an Owner. Until
        // they choose their own, they may sign in and change it, and nothing
        // else (enforced once, in Security/PasswordChangeGate.cs).
        public bool MustChangePassword { get; set; }

        // The latest successful sign-in, and the one before it. The one
        // before is what "since you last looked" compares against: by the
        // time the person sees anything, LastSignedInAt is already now.
        // Both null until the person signs in after this was added.
        public DateTimeOffset? LastSignedInAt { get; set; }
        public DateTimeOffset? PreviousSignedInAt { get; set; }

        // True only for the Salio developer, who may open the admin area.
        // Only ever set by hand in psql. EF can never write it (see
        // AppUserConfiguration), and no endpoint may grant it.
        public bool IsPlatformAdmin { get; set; }
    }
}
