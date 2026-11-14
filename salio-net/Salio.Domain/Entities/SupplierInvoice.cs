using Salio.Domain.Enums;

namespace Salio.Domain.Entities
{
    // A photo or PDF of a supplier's invoice, read into proposed lines that
    // the owner checks before anything is posted. Reading it posts nothing;
    // only committing it does, as one stock receipt.
    public class SupplierInvoice
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public DateTimeOffset UploadedAt { get; set; }
        public Guid UploadedByUserId { get; set; }

        // Where the file is kept, relative to the invoice storage folder.
        public string ImagePath { get; set; } = string.Empty;

        public SupplierInvoiceStatus Status { get; set; }

        // What was read off the invoice. Any of these may be missing.
        public string? SupplierName { get; set; }
        public string? InvoiceNumber { get; set; }
        public DateOnly? InvoiceDate { get; set; }
        public long? ExtractedTotalMinor { get; set; }

        // Everything the reader returned, kept as it came, for checking later.
        public string? RawExtractionJson { get; set; }

        // The stock receipt this invoice became, once committed. There is no
        // receipts table: a receipt is known by the Guid used as its journal
        // entry's SourceId, so this is not a foreign key.
        public Guid? CommittedStockReceiptId { get; set; }
    }
}
