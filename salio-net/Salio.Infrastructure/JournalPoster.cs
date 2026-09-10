using Salio.Domain.Entities;
using Salio.Domain.Services;

namespace Salio.Infrastructure;

/// <summary>
/// The only place in the application that writes journal entries and lines.
/// It asks LedgerService to check the entry first, then saves.
/// </summary>
public class JournalPoster
{
    private readonly SalioDbContext _db;
    private readonly LedgerService _ledger;

    public JournalPoster(SalioDbContext db, LedgerService ledger)
    {
        _db = db;
        _ledger = ledger;
    }

    public async Task PostAsync(
        JournalEntry entry,
        List<JournalLine> lines,
        CancellationToken cancellationToken)
    {
        // Throws if debits do not equal credits.
        // Also stamps every line with this entry's id.
        _ledger.PostJournal(entry, lines);

        _db.JournalEntries.Add(entry);
        _db.JournalLines.AddRange(lines);

        // One SaveChangesAsync is one database transaction: the entry and all
        // of its lines land together, or none of them do.
        await _db.SaveChangesAsync(cancellationToken);
    }
}
