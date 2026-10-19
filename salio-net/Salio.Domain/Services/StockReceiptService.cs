namespace Salio.Domain.Services;

using Salio.Domain.Entities;
using Salio.Domain.Enums;

// Goods arriving from a supplier. The same shape as SaleService: it builds
// journal lines and writes nothing. JournalPoster and StockPoster do that.
public class StockReceiptService
{
    // What the goods cost the shop. Worked out from the stock movements
    // themselves, so the journal can never book a different figure from the
    // one the shelf was valued at.
    public long TotalCost(List<StockMovement> movements)
    {
        long total = 0;

        foreach (var movement in movements)
        {
            // UnitCostMinor is ONE unit, scaled by the quantity, rounded the
            // same way a sale rounds its line totals.
            total = total + (long)Math.Round(
                movement.QuantityChange * movement.UnitCostMinor, MidpointRounding.AwayFromZero);
        }

        return total;
    }

    public List<JournalLine> BuildJournalLines(
        Guid organizationId,
        List<StockMovement> movements,
        PaymentMethod paidWith,
        Guid cashAccountId,
        Guid mpesaAccountId,
        Guid accountsPayableAccountId,
        Guid inventoryAccountId)
    {
        if (movements.Count == 0)
        {
            throw new InvalidOperationException("A stock receipt must have at least one line");
        }

        long totalCost = TotalCost(movements);

        // Goods that cost nothing move no money. A zero line would be
        // rejected by PostJournal, so say so here in words instead.
        if (totalCost <= 0)
        {
            throw new InvalidOperationException(
                "A stock receipt must cost something to post a journal entry");
        }

        return new List<JournalLine>
        {
            // The shelf is worth more.
            NewLine(organizationId, inventoryAccountId, totalCost, 0),

            // And the money went out, or is now owed to the supplier.
            NewLine(
                organizationId,
                CreditAccountFor(paidWith, cashAccountId, mpesaAccountId, accountsPayableAccountId),
                0,
                totalCost)
        };
    }

    private static Guid CreditAccountFor(
        PaymentMethod paidWith,
        Guid cashAccountId,
        Guid mpesaAccountId,
        Guid accountsPayableAccountId)
    {
        if (paidWith == PaymentMethod.Cash)
        {
            return cashAccountId;
        }

        if (paidWith == PaymentMethod.Mpesa)
        {
            return mpesaAccountId;
        }

        // Bought on account: nothing has been paid yet, the shop owes it.
        if (paidWith == PaymentMethod.Credit)
        {
            return accountsPayableAccountId;
        }

        throw new InvalidOperationException(
            paidWith + " is not a way to pay a supplier. Use Cash, MPesa or Credit");
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
