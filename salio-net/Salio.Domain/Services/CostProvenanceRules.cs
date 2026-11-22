using Salio.Domain.Entities;
using Salio.Domain.Enums;

namespace Salio.Domain.Services;

// Where a part's buying price came from. A cost a person stated (stock
// received, opening stock typed in) is a different fact from a cost a file
// import carried in, and only the first may drive pricing guidance.
//
// Derived from the movements every time, never stored, so it cannot drift
// out of step with them.
public static class CostProvenanceRules
{
    // The note OpeningStockWriter puts on every opening-stock movement. The
    // Opening stock screen, quick-add and the import all go through it.
    public const string OpeningStockNotePrefix = "Opening stock ";

    // True when the part's cost rests only on prices a person stated.
    //
    //   - at least one incoming movement states a cost a person gave:
    //     a stock receipt, or opening stock, that no import wrote; and
    //   - no imported cost is still in the part's history, unreversed.
    //
    // liveImportedMovementIds: the movements a product import wrote that
    // have not been reversed. The caller finds them (ImportedCostFinder).
    //
    // A stock-count surplus is not a stated cost: it is valued at the
    // average Salio already had. A part with no movements has no cost at all.
    public static bool CostIsConfirmed(
        IEnumerable<StockMovement> movements,
        IReadOnlySet<Guid> liveImportedMovementIds)
    {
        bool hasStatedCost = false;

        foreach (var movement in movements)
        {
            if (movement.QuantityChange <= 0 || movement.UnitCostMinor <= 0)
            {
                continue;
            }

            if (liveImportedMovementIds.Contains(movement.Id))
            {
                // An imported cost is still being averaged in.
                return false;
            }

            if (IsStatedCost(movement))
            {
                hasStatedCost = true;
            }
        }

        return hasStatedCost;
    }

    // "Last bought at": the unit cost on the newest movement where a person
    // stated what they paid (a stock receipt, a supplier invoice, opening
    // stock, quick-add with a cost). Nothing an import carried in counts.
    // Null when there is none.
    //
    // It does not depend on quantity on hand, so it survives selling out.
    // Guidance only (stop price, "you keep", the margin note): it is never
    // used to post anything. Cost of sales and stock value stay on the
    // weighted average in StockService.
    //
    // Unlike CostIsConfirmed, an imported cost elsewhere in the history does
    // not cancel it: a real purchase is a real number whatever else is on
    // the shelf. When two share a time, the later one in the list wins.
    public static long? LastBoughtAtMinor(
        IEnumerable<StockMovement> movements,
        IReadOnlySet<Guid> liveImportedMovementIds)
    {
        StockMovement? newest = null;

        foreach (var movement in movements)
        {
            if (movement.QuantityChange <= 0 || movement.UnitCostMinor <= 0)
            {
                continue;
            }

            if (liveImportedMovementIds.Contains(movement.Id) || !IsStatedCost(movement))
            {
                continue;
            }

            if (newest is null || movement.OccurredAt >= newest.OccurredAt)
            {
                newest = movement;
            }
        }

        return newest?.UnitCostMinor;
    }

    private static bool IsStatedCost(StockMovement movement)
    {
        if (movement.Type == StockMovementType.Receipt)
        {
            return true;
        }

        return movement.Type == StockMovementType.Adjustment
            && movement.Note is not null
            && movement.Note.StartsWith(OpeningStockNotePrefix, StringComparison.Ordinal);
    }
}
