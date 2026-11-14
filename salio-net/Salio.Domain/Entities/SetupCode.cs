namespace Salio.Domain.Entities
{
    // A code the developer gives one client so they can set up their own
    // shop. Six characters, works once, for one shop, and not after it
    // expires or is revoked.
    //
    // DELIBERATE, NAMED EXEMPTION FROM LOCKED RULE 2. This table is
    // platform-level, not tenant-level: a setup code exists before the shop
    // it creates, so there is no organisation to scope it by. Do not "fix"
    // this by adding a required OrganizationId. OrganizationId below is
    // nullable on purpose and is set only once the code is redeemed.
    public class SetupCode
    {
        public Guid Id { get; set; }

        // Stored UPPER CASE, without a dash. New codes are exactly six
        // characters. Older, longer codes may still sit in the table, revoked.
        public string Code { get; set; } = string.Empty;

        // The shop the code was issued for, as the developer typed it.
        public string ShopName { get; set; } = string.Empty;

        // Who the code was sent to. The developer sends it by hand; Salio
        // sends no email.
        public string Email { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }

        // Null until used. A code with a date here never works again.
        public DateTimeOffset? RedeemedAt { get; set; }

        // Null unless the developer cancelled the code. A revoked code never
        // works again.
        public DateTimeOffset? RevokedAt { get; set; }

        // The shop this code made. Null until redeemed.
        public Guid? OrganizationId { get; set; }
    }
}
