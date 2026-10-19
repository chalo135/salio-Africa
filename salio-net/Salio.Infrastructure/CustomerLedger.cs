using Microsoft.EntityFrameworkCore;
using Salio.Infrastructure.Reports;

namespace Salio.Infrastructure;

/// <summary>
/// What customers owe, read from the journal every time (locked rule 6).
/// A balance is the debits minus the credits on 1100 Accounts Receivable
/// for lines that carry that customer's id. Nothing here is stored.
/// </summary>
public class CustomerLedger
{
    public const string ReceivableCode = "1100";

    private readonly SalioDbContext _db;

    public CustomerLedger(SalioDbContext db)
    {
        _db = db;
    }

    // Every customer in the shop who has any lines, with what they owe.
    // A customer with no lines is simply not in the result: they owe 0.
    public async Task<Dictionary<Guid, long>> BalancesAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var totals = await (
            from line in _db.JournalLines.AsNoTracking()
            join account in _db.Accounts.AsNoTracking()
                on line.AccountId equals account.Id
            where line.OrganizationId == organizationId
                && account.OrganizationId == organizationId
                && account.Code == ReceivableCode
                && line.CustomerId != null
            group line by line.CustomerId
            into grouped
            select new
            {
                CustomerId = grouped.Key,
                OwedMinor = grouped.Sum(l => l.DebitMinor) - grouped.Sum(l => l.CreditMinor)
            })
            .ToListAsync(cancellationToken);

        var result = new Dictionary<Guid, long>();

        foreach (var total in totals)
        {
            if (total.CustomerId is Guid customerId)
            {
                result[customerId] = total.OwedMinor;
            }
        }

        return result;
    }

    public async Task<long> BalanceAsync(
        Guid organizationId,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        long debits = 0;
        long credits = 0;

        foreach (var row in await RowsAsync(organizationId, customerId, cancellationToken))
        {
            debits = debits + row.DebitMinor;
            credits = credits + row.CreditMinor;
        }

        return debits - credits;
    }

    // One customer's lines on 1100, oldest first: the statement.
    public async Task<List<CustomerLedgerRow>> RowsAsync(
        Guid organizationId,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        return await (
            from line in _db.JournalLines.AsNoTracking()
            join entry in _db.JournalEntries.AsNoTracking()
                on line.JournalEntryId equals entry.Id
            join account in _db.Accounts.AsNoTracking()
                on line.AccountId equals account.Id
            where line.OrganizationId == organizationId
                && entry.OrganizationId == organizationId
                && account.OrganizationId == organizationId
                && account.Code == ReceivableCode
                && line.CustomerId == customerId
            orderby entry.EntryDate, entry.CreatedAt
            select new CustomerLedgerRow
            {
                JournalEntryId = entry.Id,
                EntryDate = entry.EntryDate,
                CreatedAt = entry.CreatedAt,
                SourceType = entry.SourceType,
                SourceId = entry.SourceId,
                DebitMinor = line.DebitMinor,
                CreditMinor = line.CreditMinor
            })
            .ToListAsync(cancellationToken);
    }
}
