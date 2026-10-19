namespace Salio.Domain.Services;

using Salio.Domain.Entities;

// A credit customer paying off what they owe. Builds journal lines and
// writes nothing; JournalPoster does that.
public class CustomerPaymentService
{
    public List<JournalLine> BuildJournalLines(
        Guid organizationId,
        Guid customerId,
        string customerName,
        Guid moneyAccountId,
        Guid receivableAccountId,
        long amountMinor,
        long balanceOwedMinor)
    {
        if (amountMinor <= 0)
        {
            throw new InvalidOperationException("The amount must be more than zero");
        }

        // Paying part of the balance is normal: a rider who owes 4,000 and
        // brings 2,000. Paying more than is owed is not, because the extra
        // would sit on 1100 as money the shop owes the customer.
        if (amountMinor > balanceOwedMinor)
        {
            throw new InvalidOperationException(
                customerName + " owes " + Money.Kes(balanceOwedMinor)
                + ". A payment of " + Money.Kes(amountMinor) + " is more than that");
        }

        return new List<JournalLine>
        {
            // The money arrives in the drawer or the M-Pesa till.
            NewLine(organizationId, moneyAccountId, amountMinor, 0, null),

            // And the customer owes that much less. This line names them.
            NewLine(organizationId, receivableAccountId, 0, amountMinor, customerId)
        };
    }

    private static JournalLine NewLine(
        Guid organizationId, Guid accountId, long debit, long credit, Guid? customerId)
    {
        return new JournalLine
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            AccountId = accountId,
            DebitMinor = debit,
            CreditMinor = credit,
            Currency = "KES",
            CustomerId = customerId
        };
    }
}
