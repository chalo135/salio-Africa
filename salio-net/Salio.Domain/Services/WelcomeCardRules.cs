using Salio.Domain.Entities;
using Salio.Domain.Enums;

namespace Salio.Domain.Services;

// A delivery that cost more than the one before it.
public record CostRise(long WasMinor, long NowMinor, DateTimeOffset At);

// What the Owner's welcome card says about one part since they last
// signed in. Pure: movements in, answers out. No database.
public static class WelcomeCardRules
{
    // How far back the card looks before its first Hide.
    public const int FirstLookDays = 7;

    // The card shows changes after this. Only Hide moves ChangesSeenUntil;
    // signing in and PIN unlocks never do. Never hidden yet: the last 7 days.
    public static DateTimeOffset CardSince(DateTimeOffset? changesSeenUntil, DateTimeOffset now)
    {
        return changesSeenUntil ?? now.AddDays(-FirstLookDays);
    }

    // The newest delivery since `since`, when it cost more per unit than the
    // stated cost before it. Confirmed costs only: nothing an import carried
    // in counts on either side, and the part's cost as a whole must be
    // confirmed (CostProvenanceRules). Null otherwise.
    //
    // Delivery prices are compared, not the weighted average: "now costs you
    // 450, was 350" is what the supplier charged, which the owner can check.
    public static CostRise? CostRoseSince(
        IReadOnlyList<StockMovement> movements,
        IReadOnlySet<Guid> liveImportedMovementIds,
        DateTimeOffset since)
    {
        if (!CostProvenanceRules.CostIsConfirmed(movements, liveImportedMovementIds))
        {
            return null;
        }

        StockMovement? newest = null;

        foreach (var m in movements)
        {
            if (m.Type == StockMovementType.Receipt
                && StatedCost(m, liveImportedMovementIds)
                && m.OccurredAt > since
                && (newest is null || m.OccurredAt > newest.OccurredAt))
            {
                newest = m;
            }
        }

        if (newest is null)
        {
            return null;
        }

        StockMovement? previous = null;

        foreach (var m in movements)
        {
            bool isStated = m.Type == StockMovementType.Receipt || IsOpeningStock(m);

            if (isStated
                && m.Id != newest.Id
                && StatedCost(m, liveImportedMovementIds)
                && m.OccurredAt <= newest.OccurredAt
                && (previous is null || m.OccurredAt > previous.OccurredAt))
            {
                previous = m;
            }
        }

        if (previous is null || newest.UnitCostMinor <= previous.UnitCostMinor)
        {
            return null;
        }

        return new CostRise(previous.UnitCostMinor, newest.UnitCostMinor, newest.OccurredAt);
    }

    // When the part last went from some on the shelf to none, if that was
    // after `since` and it is still at none (or below). Null otherwise: a
    // part out of stock since before then is old news, not a change.
    public static DateTimeOffset? RanOutSince(IReadOnlyList<StockMovement> movements, DateTimeOffset since)
    {
        decimal quantity = 0;
        DateTimeOffset? ranOutAt = null;

        foreach (var m in movements.OrderBy(m => m.OccurredAt))
        {
            decimal before = quantity;
            quantity = quantity + m.QuantityChange;

            if (before > 0 && quantity <= 0 && m.OccurredAt > since)
            {
                ranOutAt = m.OccurredAt;
            }
        }

        return quantity <= 0 ? ranOutAt : null;
    }

    // Stock coming in at a cost a person stated, not one an import carried.
    private static bool StatedCost(StockMovement m, IReadOnlySet<Guid> liveImportedMovementIds)
    {
        return m.QuantityChange > 0
            && m.UnitCostMinor > 0
            && !liveImportedMovementIds.Contains(m.Id);
    }

    private static bool IsOpeningStock(StockMovement m)
    {
        return m.Type == StockMovementType.Adjustment
            && m.Note is not null
            && m.Note.StartsWith(CostProvenanceRules.OpeningStockNotePrefix, StringComparison.Ordinal);
    }
}
