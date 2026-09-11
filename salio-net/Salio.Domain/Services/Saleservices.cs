namespace Salio.Domain.Services;

using Salio.Domain.Entities;
using Salio.Domain.Enums;

public class SaleService
{
    public List<JournalLine> BuildJournalLines(
        Sale sale,
        List<SaleLine> lines,
        List<Payment> payments,
        Guid cashAccountId,
        Guid mpesaAccountId,
        Guid salesAccountId,
        Guid cogsAccountId,
        Guid inventoryAccountId)
    {
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("A sale must have at least one line");
        }

        if (payments.Count == 0)
        {
            throw new InvalidOperationException("A sale must have at least one payment");
        }

        long totalSale = 0;
        long totalCost = 0;

        foreach (var line in lines)
        {
            totalSale = totalSale + line.LineTotalMinor;
            // UnitCostMinor is the cost of ONE unit, so it has to be scaled
            // by the quantity sold. LineTotalMinor above already includes it.
            totalCost = totalCost + (long)Math.Round(
                line.Quantity * line.UnitCostMinor, MidpointRounding.AwayFromZero);
        }

        long totalPaid = 0;

        foreach (var payment in payments)
        {
            totalPaid = totalPaid + payment.AmountMinor;
        }

        // If the tender does not add up to the receipt, the journal cannot
        // balance. Catching it here says what is actually wrong, instead of
        // letting PostJournal report a mismatch of unexplained numbers.
        if (totalPaid != totalSale)
        {
            throw new InvalidOperationException(
                "Payments must add up to the sale total");
        }

        var result = new List<JournalLine>();

        // One debit per tender, so cash and M-Pesa land in their own accounts.
        // A split payment is two debits, not one.
        foreach (var payment in payments)
        {
            result.Add(NewLine(sale, AccountFor(payment, cashAccountId, mpesaAccountId), payment.AmountMinor, 0));
        }

        result.Add(NewLine(sale, salesAccountId, 0, totalSale));

        // Nothing on record means no cost to move. A zero line would be
        // rejected by PostJournal, so leave the pair out instead.
        if (totalCost > 0)
        {
            result.Add(NewLine(sale, cogsAccountId, totalCost, 0));
            result.Add(NewLine(sale, inventoryAccountId, 0, totalCost));
        }

        return result;
    }

    private static Guid AccountFor(Payment payment, Guid cashAccountId, Guid mpesaAccountId)
    {
        if (payment.Method == PaymentMethod.Cash)
        {
            return cashAccountId;
        }

        if (payment.Method == PaymentMethod.Mpesa)
        {
            return mpesaAccountId;
        }

        // Card money sits with the acquirer and credit is owed by the
        // customer. Neither has an account in the chart yet, so refuse rather
        // than book them as cash in the drawer.
        throw new InvalidOperationException(
            payment.Method + " payments are not supported yet");
    }

    private JournalLine NewLine(Sale sale, Guid accountId, long debit, long credit)
    {
        return new JournalLine
        {
            Id = Guid.NewGuid(),
            OrganizationId = sale.OrganizationId,
            AccountId = accountId,
            DebitMinor = debit,
            CreditMinor = credit,
            Currency = "KES"
        };
    }
}