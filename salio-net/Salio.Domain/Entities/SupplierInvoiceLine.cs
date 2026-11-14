namespace Salio.Domain.Entities
{
    // One line read off a supplier invoice: what the reader proposed, and
    // which of our products the owner matched it to.
    public class SupplierInvoiceLine
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public Guid SupplierInvoiceId { get; set; }

        // 1 for the first line on the invoice, 2 for the next, and so on.
        public int LineNumber { get; set; }

        // The line exactly as it was read.
        public string RawText { get; set; } = string.Empty;

        // May be empty if the reader could not make out a name.
        public string ProposedName { get; set; } = string.Empty;
        public decimal ProposedQuantity { get; set; }
        public long ProposedUnitCostMinor { get; set; }

        // The product this line is for, once matched. Null until then.
        public Guid? MatchedProductId { get; set; }

        // Whether the owner wants this line in the stock receipt.
        public bool Accepted { get; set; }
    }
}
