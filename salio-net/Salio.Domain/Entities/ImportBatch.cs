using Salio.Domain.Enums;

namespace Salio.Domain.Entities
{
    // One upload of a product file (CSV or Excel) into a shop. The products
    // it touched are listed in ImportBatchChange, so the whole batch can be
    // undone later.
    public class ImportBatch
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }

        // The name of the file as it was uploaded.
        public string FileName { get; set; } = string.Empty;

        public Guid UploadedByUserId { get; set; }
        public DateTimeOffset StartedAt { get; set; }

        // Null until every row has been written.
        public DateTimeOffset? CompletedAt { get; set; }

        public int RowsCreated { get; set; }
        public int RowsUpdated { get; set; }

        // What the file's quantities meant: opening stock, a purchase, or no
        // quantities at all. Undo reverses whichever was actually posted.
        public ImportStockBasis StockBasis { get; set; }

        // How a Purchase was paid. The same type StockReceiptWriter takes.
        // Null unless StockBasis is Purchase.
        public PaymentMethod? PaidWith { get; set; }

        // Set when someone undoes the batch. Undo posts reversing entries;
        // nothing is ever deleted.
        public DateTimeOffset? UndoneAt { get; set; }
        public Guid? UndoneByUserId { get; set; }
    }
}
