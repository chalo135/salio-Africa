namespace Salio.Domain.Services;

using Salio.Domain.Entities;

// Money going out: a shop cost, or drawings. The same shape as SaleService
// and StockReceiptService: it builds journal lines and writes nothing.
// JournalPoster does that.
public class ExpenseService
{
    public List<JournalLine> BuildJournalLines(
        Guid organizationId,
        Guid chosenAccountId,
        Guid moneyAccountId,
        long amountMinor)
    {
        if (amountMinor <= 0)
        {
            throw new InvalidOperationException("The amount must be more than zero");
        }

        return new List<JournalLine>
        {
            // What the money went on: a cost, or drawings.
            NewLine(organizationId, chosenAccountId, amountMinor, 0),

            // Where it came from: the drawer or the M-Pesa till.
            NewLine(organizationId, moneyAccountId, 0, amountMinor)
        };
    }

    private static JournalLine NewLine(Guid organizationId, Guid accountId, long debit, long credit)
    {
        return new JournalLine
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            AccountId = accountId,
            DebitMinor = debit,
            CreditMinor = credit,
            Currency = "KES"
        };
    }
}
