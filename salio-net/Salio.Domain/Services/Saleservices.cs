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
        Guid inventoryAccountId,
        Guid receivableAccountId,
        Guid? customerId)
    {
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("A sale must have at least one line");
        }

        if (payments.Count == 0)
        {
            throw new InvalidOperationException("A sale must have at least one payment");
        }

        bool onCredit = false;

        foreach (var payment in payments)
        {
            if (payment.Method == PaymentMethod.Credit)
            {
                onCredit = true;
            }
        }

        // A credit sale is credit for the whole amount. Part cash, part
        // credit is refused on purpose: the shop is ending those sales.
        // Paying off part of a debt later is a customer payment, not this.
        if (onCredit && payments.Count > 1)
        {
            throw new InvalidOperationException(
                "A credit sale is for the whole amount. It cannot be mixed with cash or M-Pesa");
        }

        if (onCredit && (customerId is null || customerId == Guid.Empty))
        {
            throw new InvalidOperationException("A credit sale needs a customer");
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
        // A split payment is two debits, not one. Credit debits Accounts
        // Receivable, and that line names the customer who owes it.
        foreach (var payment in payments)
        {
            if (payment.Method == PaymentMethod.Credit)
            {
                result.Add(NewLine(sale, receivableAccountId, payment.AmountMinor, 0, customerId));
            }
            else
            {
                result.Add(NewLine(sale, AccountFor(payment, cashAccountId, mpesaAccountId), payment.AmountMinor, 0));
            }
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

        // Card money sits with the acquirer, and there is no account for it
        // in the chart yet, so refuse rather than book it as cash in the
        // drawer. Credit never reaches here: it is handled above.
        throw new InvalidOperationException(
            payment.Method + " payments are not supported yet");
    }

    private JournalLine NewLine(Sale sale, Guid accountId, long debit, long credit, Guid? customerId = null)
    {
        return new JournalLine
        {
            Id = Guid.NewGuid(),
            OrganizationId = sale.OrganizationId,
            AccountId = accountId,
            DebitMinor = debit,
            CreditMinor = credit,
            Currency = "KES",
            CustomerId = customerId
        };
    }
}