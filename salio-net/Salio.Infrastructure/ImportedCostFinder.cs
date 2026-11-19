using Microsoft.EntityFrameworkCore;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;

namespace Salio.Infrastructure;

// One part a product import created, and the stock it brought in.
public class ImportedStock
{
    public Guid ImportBatchId { get; set; }
    public Guid ImportBatchChangeId { get; set; }
    public Guid ProductId { get; set; }

    // OpeningStock or Purchase, as the batch recorded it.
    public ImportStockBasis StockBasis { get; set; }

    // The movements the import wrote for this part.
    public List<StockMovement> Movements { get; set; } = new();

    // The journal entry those movements belong to. For opening stock it is
    // this part's own entry; for a purchase it is the one receipt shared by
    // every part in the batch. Null if none was found.
    public Guid? JournalEntryId { get; set; }

    // True once that journal entry has a Reversal pointing at it.
    public bool Reversed { get; set; }
}

// Finds the stock a product import wrote, using only what is already stored.
// Nothing new is recorded on a movement: the same two markers the import's
// own undo uses (ProductImportService.UndoAsync) identify it.
//
//   opening stock: an Adjustment whose note is "Opening stock " + the first
//                  8 characters of the ImportBatchChange id, and a journal
//                  entry (StockAdjustment) whose SourceId is that change id;
//   purchase:      a Receipt whose note is StockReceiptWriter.NoteFor(batch
//                  id), and a journal entry (Purchase) whose SourceId is the
//                  batch id.
//
// Reads only. Never writes.
public class ImportedCostFinder
{
    private readonly SalioDbContext _db;

    public ImportedCostFinder(SalioDbContext db)
    {
        _db = db;
    }

    public async Task<List<ImportedStock>> FindAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var created = await (
            from change in _db.ImportBatchChanges.AsNoTracking()
            join batch in _db.ImportBatches.AsNoTracking() on change.ImportBatchId equals batch.Id
            where change.OrganizationId == organizationId
                && batch.OrganizationId == organizationId
                && change.ChangeType == ImportChangeType.Created
            select new { change.Id, change.ImportBatchId, change.ProductId, batch.StockBasis })
            .ToListAsync(cancellationToken);

        if (created.Count == 0)
        {
            return new List<ImportedStock>();
        }

        var productIds = created.Select(c => c.ProductId).Distinct().ToList();

        var movements = await _db.StockMovements.AsNoTracking()
            .Where(m => m.OrganizationId == organizationId && productIds.Contains(m.ProductId))
            .ToListAsync(cancellationToken);

        var changeIds = created.Select(c => c.Id).ToList();
        var batchIds = created.Select(c => c.ImportBatchId).Distinct().ToList();

        var entries = await _db.JournalEntries.AsNoTracking()
            .Where(e => e.OrganizationId == organizationId
                && e.SourceId != null
                && ((e.SourceType == SourceType.StockAdjustment && changeIds.Contains(e.SourceId.Value))
                    || (e.SourceType == SourceType.Purchase && batchIds.Contains(e.SourceId.Value))))
            .Select(e => new { e.Id, e.SourceType, e.SourceId })
            .ToListAsync(cancellationToken);

        var entryIds = entries.Select(e => e.Id).ToList();

        var reversedEntryIds = await _db.JournalEntries.AsNoTracking()
            .Where(e => e.OrganizationId == organizationId
                && e.SourceType == SourceType.Reversal
                && e.SourceId != null
                && entryIds.Contains(e.SourceId.Value))
            .Select(e => e.SourceId!.Value)
            .ToListAsync(cancellationToken);

        var result = new List<ImportedStock>();

        foreach (var change in created)
        {
            string openingNote = CostProvenanceRules.OpeningStockNotePrefix + change.Id.ToString()[..8];
            string receiptNote = StockReceiptWriter.NoteFor(change.ImportBatchId, null);

            var own = movements
                .Where(m => m.ProductId == change.ProductId
                    && ((m.Type == StockMovementType.Adjustment && m.Note == openingNote)
                        || (m.Type == StockMovementType.Receipt && m.Note == receiptNote)))
                .ToList();

            var entry = entries.FirstOrDefault(e =>
                (e.SourceType == SourceType.StockAdjustment && e.SourceId == change.Id)
                || (e.SourceType == SourceType.Purchase && e.SourceId == change.ImportBatchId));

            result.Add(new ImportedStock
            {
                ImportBatchId = change.ImportBatchId,
                ImportBatchChangeId = change.Id,
                ProductId = change.ProductId,
                StockBasis = change.StockBasis,
                Movements = own,
                JournalEntryId = entry?.Id,
                Reversed = entry is not null && reversedEntryIds.Contains(entry.Id)
            });
        }

        return result;
    }

    // The ids of imported movements still counted in a part's cost: written
    // by an import and not reversed since. What CostProvenanceRules needs.
    public async Task<HashSet<Guid>> LiveImportedMovementIdsAsync(
        Guid organizationId, CancellationToken cancellationToken)
    {
        var ids = new HashSet<Guid>();

        foreach (var imported in await FindAsync(organizationId, cancellationToken))
        {
            if (imported.Reversed)
            {
                continue;
            }

            foreach (var movement in imported.Movements)
            {
                ids.Add(movement.Id);
            }
        }

        return ids;
    }
}
