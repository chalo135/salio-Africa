namespace Salio.Domain.Entities
{
    // A person who can log in. One row per human, not per shop.
    //
    // This is the ONLY entity in Salio without an OrganizationId, and so the
    // only one exempt from locked rule 2. It is exempt because of locked
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
        // never deleted.
        public bool IsActive { get; set; } = true;
    }
}
