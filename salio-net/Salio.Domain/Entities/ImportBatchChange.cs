using Salio.Domain.Enums;

namespace Salio.Domain.Entities
{
    // One product that an import created or changed. When opening stock is
    // posted for a created product, the journal entry's SourceId is this
    // row's Id, so undo can find and reverse exactly that entry.
    public class ImportBatchChange
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public Guid ImportBatchId { get; set; }
        public Guid ProductId { get; set; }
        public ImportChangeType ChangeType { get; set; }

        // The product's name, selling price, category, barcode and reorder
        // level before the import changed them, so undo can put them back.
        // Null for a product the import created.
        public string? PreviousValuesJson { get; set; }
    }
}
