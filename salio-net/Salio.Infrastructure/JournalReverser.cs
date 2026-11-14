using Microsoft.EntityFrameworkCore;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;

namespace Salio.Infrastructure;

// Undoes an entry the only way the books allow: by posting its mirror image
// (decision D5). Each debit of the original becomes a credit of the same
// amount on the same account, and each credit a debit. It computes no
// amount of its own, and it never edits or deletes the original: both stay
// in the books, side by side.
public class JournalReverser
{
    private readonly SalioDbContext _db;
    private readonly JournalPoster _journalPoster;
    private readonly StockPoster _stockPoster;

    public JournalReverser(SalioDbContext db, JournalPoster journalPoster, StockPoster stockPoster)
    {
        _db = db;
        _journalPoster = journalPoster;
        _stockPoster = stockPoster;
    }

    // Kenya has one time zone and no daylight saving.
    public static DateOnly TodayInNairobi()
    {
        return StockMovementDates.KenyaDateOf(DateTimeOffset.UtcNow);
    }

    // Returns the id of the reversing entry. Throws InvalidOperationException,
    // in words, if the entry is not in this shop or was reversed already.
    //
    // reversalDate: the date of the reversing entry, normally TodayInNairobi().
    // The caller passes the same date to ReverseStockAsync, so an undo is one
    // transaction with one date.
    public async Task<Guid> ReverseAsync(
        Guid organizationId,
        Guid journalEntryId,
        string reason,
        DateOnly reversalDate,
        CancellationToken cancellationToken)
    {
        var original = await _db.JournalEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.Id == journalEntryId && e.OrganizationId == organizationId,
                cancellationToken);

        if (original is null)
        {
            throw new InvalidOperationException("That journal entry is not in this shop.");
        }

        // A reversal cannot be reversed again by this route, and an entry can
        // only be reversed once: twice would undo the undo.
        if (original.SourceType == SourceType.Reversal)
        {
            throw new InvalidOperationException("A reversing entry cannot itself be reversed here.");
        }

        bool alreadyReversed = await _db.JournalEntries
            .AnyAsync(e => e.OrganizationId == organizationId
                && e.SourceType == SourceType.Reversal
                && e.SourceId == journalEntryId, cancellationToken);

        if (alreadyReversed)
        {
            throw new InvalidOperationException("This entry has already been reversed.");
        }

        var originalLines = await _db.JournalLines
            .AsNoTracking()
            .Where(l => l.OrganizationId == organizationId && l.JournalEntryId == journalEntryId)
            .ToListAsync(cancellationToken);

        if (originalLines.Count == 0)
        {
            throw new InvalidOperationException("That journal entry has no lines to reverse.");
        }

        var mirrorLines = new List<JournalLine>();

        foreach (var line in originalLines)
        {
            // The same account, the same amount, the other column.
            mirrorLines.Add(new JournalLine
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                AccountId = line.AccountId,
                DebitMinor = line.CreditMinor,
                CreditMinor = line.DebitMinor,
                Currency = line.Currency,
                CustomerId = line.CustomerId
            });
        }

        // Dated today: the original's date may be in a period already filed
        // with KRA, and today is open (JournalPoster refuses a filed period).
        var reversal = new JournalEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            EntryDate = reversalDate,
            Description = "Reversal of: " + original.Description + " (" + reason + ")",
            SourceType = SourceType.Reversal,
            SourceId = original.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _journalPoster.PostAsync(reversal, mirrorLines, cancellationToken);

        return reversal.Id;
    }

    // The stock half of an undo: one opposing Adjustment for each movement
    // given, the same unit cost and the quantity negated. The originals stay.
    // reversalDate: the date of the reversing journal entry these belong to.
    public async Task ReverseStockAsync(
        Guid organizationId,
        List<StockMovement> originals,
        string note,
        DateOnly reversalDate,
        CancellationToken cancellationToken)
    {
        // One date: the opposite movements tell the same day as the
        // reversing entry (see StockMovementDates).
        DateTimeOffset occurredAt = StockMovementDates.OccurredAtFor(reversalDate, DateTimeOffset.UtcNow);
        var opposites = new List<StockMovement>();

        foreach (var movement in originals)
        {
            if (movement.OrganizationId != organizationId)
            {
                throw new InvalidOperationException("That stock movement is not in this shop.");
            }

            opposites.Add(new StockMovement
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                ProductId = movement.ProductId,
                Type = StockMovementType.Adjustment,
                QuantityChange = -movement.QuantityChange,
                UnitCostMinor = movement.UnitCostMinor,
                OccurredAt = occurredAt,
                Note = note
            });
        }

        await _stockPoster.PostAsync(opposites, cancellationToken);
    }
}
