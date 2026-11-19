using Salio.Domain.Entities;
using Salio.Domain.Enums;

namespace Salio.Domain.Services;

public enum OpeningStockReentryDecision
{
    // Every part on the list may have its opening stock entered again.
    Allowed,

    // At least one part on the list may not: the usual refusal applies.
    NotEligible,

    // The parts may, but the date is before their imported stock was
    // reversed. See TooEarly below.
    TooEarly
}

// The one exception to "no opening stock after the shop's first sale",
// decided 19 November 2026 (option a of COST-PROVENANCE-REPORT.md).
//
// A part whose opening stock came from a product import, and whose imported
// stock has been reversed, may have its opening stock entered once more, at
// the real cost the owner states. It was already on the shelf, so it is
// still the owner's (Dr 1200 Inventory, Cr 3000 Owner's Equity), never a
// purchase. Every other part keeps the rule.
public static class OpeningStockReentryRules
{
    // True when this part may have its opening stock entered again.
    //
    //   - at least one imported opening-stock movement of it was reversed; and
    //   - every opening-stock movement it has is one of those: none was
    //     entered since, so this happens once.
    //
    // reversedImportedMovementIds: the movements an import wrote whose
    // journal entry has been reversed (ImportedCostFinder).
    public static bool MayReenter(
        IEnumerable<StockMovement> productMovements,
        IReadOnlySet<Guid> reversedImportedMovementIds)
    {
        bool hasReversedImport = false;

        foreach (var movement in productMovements)
        {
            bool isOpeningStock = movement.Type == StockMovementType.Adjustment
                && movement.QuantityChange > 0
                && movement.Note is not null
                && movement.Note.StartsWith(CostProvenanceRules.OpeningStockNotePrefix, StringComparison.Ordinal);

            if (!isOpeningStock)
            {
                continue;
            }

            if (!reversedImportedMovementIds.Contains(movement.Id))
            {
                // Opening stock that is still standing: entered already.
                return false;
            }

            hasReversedImport = true;
        }

        return hasReversedImport;
    }

    // Whether a list of parts may be entered as opening stock in a shop that
    // has already sold.
    //
    // reversedOn: each part that MayReenter, with the date its imported
    // stock was reversed. The opening stock must not be dated before that:
    // the weighted average replays movements by date, and real stock dated
    // before the reversal would be averaged together with the invented cost.
    public static OpeningStockReentryDecision Decide(
        IEnumerable<Guid> productIds,
        IReadOnlyDictionary<Guid, DateOnly> reversedOn,
        DateOnly openingDate)
    {
        bool tooEarly = false;
        bool any = false;

        foreach (Guid productId in productIds)
        {
            any = true;

            if (!reversedOn.TryGetValue(productId, out DateOnly reversed))
            {
                return OpeningStockReentryDecision.NotEligible;
            }

            if (openingDate < reversed)
            {
                tooEarly = true;
            }
        }

        if (!any)
        {
            return OpeningStockReentryDecision.NotEligible;
        }

        return tooEarly ? OpeningStockReentryDecision.TooEarly : OpeningStockReentryDecision.Allowed;
    }

    // The latest date any of these parts had its imported stock reversed:
    // the earliest the opening stock may be dated.
    public static DateOnly EarliestDate(IEnumerable<Guid> productIds, IReadOnlyDictionary<Guid, DateOnly> reversedOn)
    {
        DateOnly earliest = DateOnly.MinValue;

        foreach (Guid productId in productIds)
        {
            if (reversedOn.TryGetValue(productId, out DateOnly reversed) && reversed > earliest)
            {
                earliest = reversed;
            }
        }

        return earliest;
    }
}
