namespace Salio.Domain.Services;

using Salio.Domain.Entities;

public class LedgerService
{
    public void PostJournal(JournalEntry entry, List<JournalLine> lines)
    {
        if (lines.Count == 0)
        {
            throw new InvalidOperationException(
                "A journal entry must have at least one line");
        }

        foreach (var line in lines)
        {
            if (line.DebitMinor < 0 || line.CreditMinor < 0)
            {
                throw new InvalidOperationException(
                    "A journal line cannot have a negative amount");
            }

            if (line.DebitMinor == 0 && line.CreditMinor == 0)
            {
                throw new InvalidOperationException(
                    "A journal line must have a debit or a credit");
            }

            if (line.DebitMinor != 0 && line.CreditMinor != 0)
            {
                throw new InvalidOperationException(
                    "A journal line cannot have both a debit and a credit");
            }
        }

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
