namespace Salio.Domain.Entities
{
    // One row for each thing a platform admin did: issued a code, revoked
    // one, looked at a shop. Rows are only ever added.
    //
    // DELIBERATE, NAMED EXEMPTION FROM LOCKED RULE 2. This table is
    // platform-level, not tenant-level: it records the admin's actions across
    // all shops, and some actions (issuing a code) happen before any shop
    // exists. Do not "fix" this by adding a required OrganizationId.
    public class AdminAuditLog
    {
        public Guid Id { get; set; }

        // The admin who did it.
        public Guid AdminUserId { get; set; }

        // A short name for what was done, e.g. "IssueSetupCode".
        public string Action { get; set; } = string.Empty;

        // The shop it was about, if any.
        public Guid? TargetOrganizationId { get; set; }

        // The setup code it was about, if any.
        public Guid? TargetSetupCodeId { get; set; }

        public DateTimeOffset At { get; set; }

        // Plain words about what happened. Never a client's sales figures.
        public string Detail { get; set; } = string.Empty;
    }
}
