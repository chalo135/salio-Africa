using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Salio.Api.Dtos;
using Salio.Api.Security;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Infrastructure;

namespace Salio.Api.Controllers;

// Credit customers. Both roles may add and list them: a cashier meets a new
// credit customer at the counter and needs to see what they already owe.
// The full statement is Owner only.
[ApiController]
[Authorize]
[Route("api/customers")]
public class CustomersController : ControllerBase
{
    private readonly SalioDbContext _db;
    private readonly CustomerLedger _ledger;
    private readonly OrganizationGuard _guard;

    public CustomersController(SalioDbContext db, CustomerLedger ledger, OrganizationGuard guard)
    {
        _db = db;
        _ledger = ledger;
        _guard = guard;
    }

    [HttpPost]
    public async Task<ActionResult<CustomerResponse>> Create(
        CreateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var access = await _guard.ForAsync(User, request.OrganizationId, cancellationToken);

        if (!access.IsAllowed)
        {
            return OrganizationGuard.Deny(access);
        }

        string name = request.Name.Trim();
        string phone = request.Phone.Trim();

        if (name == "" || phone == "")
        {
            return BadRequest(Rejected("A customer needs a name and a phone number"));
        }

        // One customer per phone number in a shop, so a returning customer
        // cannot end up with two separate balances. Say who already has it.
        var existing = await _db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.OrganizationId == access.OrganizationId && c.Phone == phone,
                cancellationToken);

        if (existing is not null)
        {
            return BadRequest(Rejected(
                phone + " already belongs to " + existing.Name + ". Choose them from the list instead"));
        }

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            OrganizationId = access.OrganizationId,
            Name = name,
            Phone = phone,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };

        _db.Customers.Add(customer);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two people adding the same number at the same moment: the
            // unique index on (shop, phone) stops the second.
            return BadRequest(Rejected(phone + " was added by someone else just now. Choose them from the list"));
        }

        return new CustomerResponse
        {
            Id = customer.Id,
            Name = customer.Name,
            Phone = customer.Phone,
            BalanceMinor = 0
        };
    }

    [HttpGet]
    public async Task<ActionResult<List<CustomerResponse>>> GetAll(
        [FromQuery] Guid? organizationId,
        CancellationToken cancellationToken)
    {
        var access = await _guard.ForAsync(User, organizationId, cancellationToken);

        if (!access.IsAllowed)
        {
            return OrganizationGuard.Deny(access);
        }

        var customers = await _db.Customers
            .AsNoTracking()
            .Where(c => c.OrganizationId == access.OrganizationId && c.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync(cancellationToken);

        // Every balance in one query, matched up in memory.
        var balances = await _ledger.BalancesAsync(access.OrganizationId, cancellationToken);

        var response = new List<CustomerResponse>();

        foreach (var customer in customers)
        {
            response.Add(new CustomerResponse
            {
                Id = customer.Id,
                Name = customer.Name,
                Phone = customer.Phone,
                BalanceMinor = balances.GetValueOrDefault(customer.Id)
            });
        }

        return response;
    }

    // Credit sales and payments, oldest first, with the running balance.
    // Owner only: it is the shop's money (locked rule 13).
    [HttpGet("{id:guid}/statement")]
    public async Task<ActionResult<CustomerStatementResponse>> GetStatement(
        Guid id,
        [FromQuery] Guid? organizationId,
        CancellationToken cancellationToken)
    {
        var access = await _guard.ForAsync(User, organizationId, cancellationToken);

        if (!access.IsAllowed)
        {
            return OrganizationGuard.Deny(access);
        }

        if (!access.IsOwner)
        {
            return OrganizationGuard.OwnerOnly();
        }

        var customer = await _db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.Id == id && c.OrganizationId == access.OrganizationId,
                cancellationToken);

        if (customer is null)
        {
            return NotFound();
        }

        var rows = await _ledger.RowsAsync(access.OrganizationId, customer.Id, cancellationToken);

        // Sale numbers for the sales, in one query.
        var saleIds = new List<Guid>();
        var paymentEntryIds = new List<Guid>();

        foreach (var row in rows)
        {
            if (row.SourceType == SourceType.Sale && row.SourceId is Guid saleId)
            {
                saleIds.Add(saleId);
            }

            if (row.SourceType == SourceType.CustomerPayment)
            {
                paymentEntryIds.Add(row.JournalEntryId);
            }
        }

        var sales = await _db.Sales
            .AsNoTracking()
            .Where(s => s.OrganizationId == access.OrganizationId && saleIds.Contains(s.Id))
            .Select(s => new { s.Id, s.SaleNumber })
            .ToListAsync(cancellationToken);

        // For each payment, the account the money went into: the debit
        // line of the same entry. One query for all of them.
        var paymentMoney = await (
            from line in _db.JournalLines.AsNoTracking()
            join account in _db.Accounts.AsNoTracking()
                on line.AccountId equals account.Id
            where line.OrganizationId == access.OrganizationId
                && paymentEntryIds.Contains(line.JournalEntryId)
                && line.DebitMinor > 0
            select new { line.JournalEntryId, account.Code })
            .ToListAsync(cancellationToken);

        var lines = new List<CustomerStatementLineResponse>();
        long balance = 0;

        foreach (var row in rows)
        {
            balance = balance + row.DebitMinor - row.CreditMinor;

            string kind;
            string reference = "";

            if (row.SourceType == SourceType.Sale)
            {
                kind = "Sale";

                foreach (var sale in sales)
                {
                    if (sale.Id == row.SourceId)
                    {
                        reference = sale.SaleNumber;
                    }
                }
            }
            else if (row.SourceType == SourceType.CustomerPayment)
            {
                kind = "Payment";

                foreach (var money in paymentMoney)
                {
                    if (money.JournalEntryId == row.JournalEntryId)
                    {
                        reference = money.Code == "1010" ? "Mpesa" : "Cash";
                    }
                }
            }
            else
            {
                // Anything else on 1100 for this customer, such as a manual
                // journal entry. Shown, so the balance always adds up.
                kind = "Adjustment";
            }

            lines.Add(new CustomerStatementLineResponse
            {
                Date = row.EntryDate,
                Kind = kind,
                Reference = reference,
                ChargedMinor = row.DebitMinor,
                PaidMinor = row.CreditMinor,
                BalanceAfterMinor = balance
            });
        }

        return new CustomerStatementResponse
        {
            CustomerId = customer.Id,
            Name = customer.Name,
            Phone = customer.Phone,
            BalanceMinor = balance,
            Lines = lines
        };
    }

    private static ProblemDetails Rejected(string detail) => new()
    {
        Title = "Refused",
        Detail = detail,
        Status = StatusCodes.Status400BadRequest
    };
}
