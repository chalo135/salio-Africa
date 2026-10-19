using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Salio.Api.Dtos;
using Salio.Api.Security;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;
using Salio.Infrastructure;

namespace Salio.Api.Controllers;

// A credit customer paying off what they owe. Owner only: the only screen
// that records one is the Owner's "who owes what" screen.
[ApiController]
[Authorize]
[Route("api/customer-payments")]
public class CustomerPaymentsController : ControllerBase
{
    private const string CashCode = "1000";
    private const string MpesaCode = "1010";

    private readonly SalioDbContext _db;
    private readonly CustomerPaymentService _paymentService;
    private readonly CustomerLedger _ledger;
    private readonly JournalPoster _journalPoster;
    private readonly OrganizationGuard _guard;

    public CustomerPaymentsController(
        SalioDbContext db,
        CustomerPaymentService paymentService,
        CustomerLedger ledger,
        JournalPoster journalPoster,
        OrganizationGuard guard)
    {
        _db = db;
        _paymentService = paymentService;
        _ledger = ledger;
        _journalPoster = journalPoster;
        _guard = guard;
    }

    [HttpPost]
    public async Task<ActionResult<CustomerPaymentResponse>> Create(
        CreateCustomerPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var access = await _guard.ForAsync(User, request.OrganizationId, cancellationToken);

        if (!access.IsAllowed)
        {
            return OrganizationGuard.Deny(access);
        }

        if (!access.IsOwner)
        {
            return OrganizationGuard.OwnerOnly();
        }

        string moneyCode;

        if (request.PaidWith == PaymentMethod.Cash)
        {
            moneyCode = CashCode;
        }
        else if (request.PaidWith == PaymentMethod.Mpesa)
        {
            moneyCode = MpesaCode;
        }
        else
        {
            return BadRequest(Rejected("A payment must be Cash or M-Pesa"));
        }

        // Rule 9: the customer is looked up inside this shop only.
        var customer = await _db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.Id == request.CustomerId && c.OrganizationId == access.OrganizationId,
                cancellationToken);

        if (customer is null)
        {
            return BadRequest(Rejected("That customer is not in this shop"));
        }

        var accounts = await _db.Accounts
            .AsNoTracking()
            .Where(a => a.OrganizationId == access.OrganizationId
                && a.IsActive
                && (a.Code == moneyCode || a.Code == CustomerLedger.ReceivableCode))
            .ToListAsync(cancellationToken);

        Guid moneyAccountId = Guid.Empty;
        Guid receivableAccountId = Guid.Empty;

        foreach (var account in accounts)
        {
            if (account.Code == moneyCode)
            {
                moneyAccountId = account.Id;
            }

            if (account.Code == CustomerLedger.ReceivableCode)
            {
                receivableAccountId = account.Id;
            }
        }

        if (moneyAccountId == Guid.Empty || receivableAccountId == Guid.Empty)
        {
            return BadRequest(Rejected(
                "This organisation is missing account " + moneyCode + " or 1100, which a payment needs"));
        }

        long owedMinor = await _ledger.BalanceAsync(access.OrganizationId, customer.Id, cancellationToken);

        var entry = new JournalEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = access.OrganizationId,
            EntryDate = request.PaymentDate,
            Description = "Payment from " + customer.Name,
            SourceType = SourceType.CustomerPayment,

            // The customer is the source: a payment is not a record of its own.
            SourceId = customer.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            // Refuses zero, and anything over what is owed, naming the balance.
            var lines = _paymentService.BuildJournalLines(
                access.OrganizationId, customer.Id, customer.Name, moneyAccountId, receivableAccountId,
                request.AmountMinor, owedMinor);

            await _journalPoster.PostAsync(entry, lines, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(Rejected(ex.Message));
        }

        return new CustomerPaymentResponse
        {
            Id = entry.Id,
            CustomerId = customer.Id,
            CustomerName = customer.Name,
            PaymentDate = entry.EntryDate,
            AmountMinor = request.AmountMinor,
            PaidWith = request.PaidWith,
            BalanceAfterMinor = owedMinor - request.AmountMinor
        };
    }

    [HttpGet]
    public async Task<ActionResult<List<CustomerPaymentResponse>>> GetAll(
        [FromQuery] Guid? organizationId,
        [FromQuery][BindRequired] DateOnly from,
        [FromQuery][BindRequired] DateOnly to,
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

        if (to < from)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Bad period",
                Detail = "The period ends before it starts",
                Status = StatusCodes.Status400BadRequest
            });
        }

        // 'from' is a LINQ keyword, so copy the dates before using them.
        DateOnly fromDate = from;
        DateOnly toDate = to;

        // Each payment's two lines: the money in (debit) and the customer's
        // line on 1100 (credit, carrying the customer id). One query.
        var lines = await (
            from line in _db.JournalLines.AsNoTracking()
            join entry in _db.JournalEntries.AsNoTracking()
                on line.JournalEntryId equals entry.Id
            join account in _db.Accounts.AsNoTracking()
                on line.AccountId equals account.Id
            where line.OrganizationId == access.OrganizationId
                && entry.OrganizationId == access.OrganizationId
                && entry.SourceType == SourceType.CustomerPayment
                && entry.EntryDate >= fromDate
                && entry.EntryDate <= toDate
            select new
            {
                EntryId = entry.Id,
                entry.EntryDate,
                entry.CreatedAt,
                account.Code,
                line.DebitMinor,
                line.CustomerId
            })
            .ToListAsync(cancellationToken);

        var customers = await _db.Customers
            .AsNoTracking()
            .Where(c => c.OrganizationId == access.OrganizationId)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(cancellationToken);

        var response = new List<CustomerPaymentResponse>();

        // The debit line of each entry is the money; build one row from it,
        // then find the same entry's customer line.
        foreach (var moneyLine in lines.Where(l => l.DebitMinor > 0)
            .OrderByDescending(l => l.EntryDate).ThenByDescending(l => l.CreatedAt))
        {
            Guid customerId = Guid.Empty;

            foreach (var line in lines)
            {
                if (line.EntryId == moneyLine.EntryId && line.CustomerId is Guid id)
                {
                    customerId = id;
                }
            }

            string customerName = "";

            foreach (var customer in customers)
            {
                if (customer.Id == customerId)
                {
                    customerName = customer.Name;
                }
            }

            response.Add(new CustomerPaymentResponse
            {
                Id = moneyLine.EntryId,
                CustomerId = customerId,
                CustomerName = customerName,
                PaymentDate = moneyLine.EntryDate,
                AmountMinor = moneyLine.DebitMinor,
                PaidWith = moneyLine.Code == MpesaCode ? PaymentMethod.Mpesa : PaymentMethod.Cash,
                BalanceAfterMinor = null
            });
        }

        return response;
    }

    private static ProblemDetails Rejected(string detail) => new()
    {
        Title = "The payment was rejected",
        Detail = detail,
        Status = StatusCodes.Status400BadRequest
    };
}
