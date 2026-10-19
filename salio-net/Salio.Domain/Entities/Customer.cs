namespace Salio.Domain.Entities;

// Someone the shop sells to on credit. What they owe is never stored here:
// it is worked out from the journal lines on 1100 Accounts Receivable that
// carry this customer's id (locked rule 6).
public class Customer
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }

    // A customer with history is switched off, never deleted: their sales
    // and payments stay in the books.
    public bool IsActive { get; set; } = true;

    // eTIMS / KRA — nullable and unused until KRA integration is built.
    public string? KraPin { get; set; }
    public string? EtimsBranchCd { get; set; }
}
