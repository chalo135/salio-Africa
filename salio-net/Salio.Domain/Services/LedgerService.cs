namespace Salio.Domain.Services;

using Salio.Domain.Entities;

public class LedgerService
{
    public void PostJournal(JournalEntry entry, List<JournalLine> lines)
    {
        long totalDebits = 0;
        long totalCredits = 0;

        foreach (var line in lines)
        {
            totalDebits = totalDebits + line.DebitMinor;
            totalCredits = totalCredits + line.CreditMinor;
        }

        if (totalDebits != totalCredits)
        {
            throw new InvalidOperationException("Debits must equal credits");
        }

        foreach (var line in lines)
        {
            line.JournalEntryId = entry.Id;
        }
    }
}
