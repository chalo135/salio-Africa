namespace Salio.Domain.Entities
{
    // A phone that has signed in with email and password, and so may be
    // unlocked with a PIN afterwards. The salio_device cookie proves the
    // phone is this one.
    //
    // No OrganizationId, like AppUser: a device belongs to a person, not a
    // shop (locked rule 10). Which shops the person can touch is still
    // decided by their memberships after unlocking, exactly as after a
    // password login.
    //
    // Never deleted. A device that must stop working gets RevokedAt set.
    public class TrustedDevice
    {
        public Guid Id { get; set; }
        public Guid AppUserId { get; set; }

        // Only a hash of the cookie's secret, made by PasswordService
        // (locked rule 11). The secret itself lives only in the cookie.
        public string SecretHash { get; set; } = string.Empty;

        // Null until the person chooses a PIN on this phone. Also a hash.
        public string? PinHash { get; set; }

        // Wrong PINs in a row. Back to 0 after a right one.
        public int FailedPinAttempts { get; set; }

        // The user's SecurityStamp when this device was trusted. If the
        // user's stamp changes ("sign out every device"), this device is dead.
        public string SecurityStamp { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? LastUsedAt { get; set; }
        public DateTimeOffset? RevokedAt { get; set; }
    }
}
