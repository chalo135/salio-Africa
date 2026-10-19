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

// Money going out of the drawer or the M-Pesa till: shop costs, and the
// owner's drawings. Owner only: it is money (locked rule 13).
[ApiController]
[Authorize]
[Route("api/expenses")]
public class ExpensesController : ControllerBase
{
    private const string CashCode = "1000";
    private const string MpesaCode = "1010";
    private const string DrawingsCode = "3100";
    private const string CostOfGoodsSoldCode = "5000";

    private readonly SalioDbContext _db;
    private readonly ExpenseService _expenseService;
    private readonly JournalPoster _journalPoster;
    private readonly OrganizationGuard _guard;

    public ExpensesController(
        SalioDbContext db,
        ExpenseService expenseService,
        JournalPoster journalPoster,
        OrganizationGuard guard)
    {
        _db = db;
        _expenseService = expenseService;
        _journalPoster = journalPoster;
        _guard = guard;
    }

    [HttpPost]
    public async Task<ActionResult<ExpenseResponse>> Create(
        CreateExpenseRequest request,
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

        if (request.AmountMinor <= 0)
        {
            return BadRequest(Rejected("The amount must be more than zero"));
        }

        string moneyCode;

        if (request.PaidFrom == PaymentMethod.Cash)
        {
            moneyCode = CashCode;
        }
        else if (request.PaidFrom == PaymentMethod.Mpesa)
        {
            moneyCode = MpesaCode;
        }
        else
        {
            return BadRequest(Rejected("The money must come from Cash or M-Pesa"));
        }

        // Rule 9: the account is looked up inside this shop only. Another
        // shop's account id and an id that does not exist get the same answer.
        var chosen = await _db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.Id == request.AccountId
                    && a.OrganizationId == access.OrganizationId
                    && a.IsActive,
                cancellationToken);

        if (chosen is null)
        {
            return BadRequest(Rejected("That category is not in this shop"));
        }

        if (!IsPayableHere(chosen))
        {
            return BadRequest(Rejected(
                "Money out can only be recorded against a shop cost or Drawings. "
                + chosen.Name + " is neither."));
        }

        var moneyAccount = await _db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.OrganizationId == access.OrganizationId
                    && a.Code == moneyCode
                    && a.IsActive,
                cancellationToken);

        if (moneyAccount is null)
        {
            return BadRequest(Rejected(
                "This organisation is missing account " + moneyCode + ", which this payment needs"));
        }

        bool isDrawings = chosen.Code == DrawingsCode;
        string? note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();

        var entry = new JournalEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = access.OrganizationId,
            EntryDate = request.ExpenseDate,

            // The note if there is one, else the category, so the journal
            // always says something readable.
            Description = note ?? chosen.Name,
            SourceType = isDrawings ? SourceType.Drawings : SourceType.Expense,
            SourceId = null,
            CreatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            var lines = _expenseService.BuildJournalLines(
                access.OrganizationId, chosen.Id, moneyAccount.Id, request.AmountMinor);

            // One PostAsync is one SaveChangesAsync: the entry and both lines
            // land together, or none of them do.
            await _journalPoster.PostAsync(entry, lines, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // A zero amount, or a period closed by a filed tax return.
            return BadRequest(Rejected(ex.Message));
        }

        return new ExpenseResponse
        {
            Id = entry.Id,
            ExpenseDate = entry.EntryDate,
            AccountId = chosen.Id,
            CategoryName = chosen.Name,
            IsDrawings = isDrawings,
            AmountMinor = request.AmountMinor,
            PaidFrom = request.PaidFrom,
            Note = note
        };
    }

    [HttpGet]
    public async Task<ActionResult<List<ExpenseResponse>>> GetAll(
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

        var entries = await _db.JournalEntries
            .AsNoTracking()
            .Where(e => e.OrganizationId == access.OrganizationId
                && (e.SourceType == SourceType.Expense || e.SourceType == SourceType.Drawings)
                && e.EntryDate >= fromDate
                && e.EntryDate <= toDate)
            .OrderByDescending(e => e.EntryDate)
            .ThenByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        var entryIds = new List<Guid>();

        foreach (var entry in entries)
        {
            entryIds.Add(entry.Id);
        }

        // Every line for the whole list in one query, then matched up in
        // memory, rather than one query per entry.
        var lines = await _db.JournalLines
            .AsNoTracking()
            .Where(l => l.OrganizationId == access.OrganizationId && entryIds.Contains(l.JournalEntryId))
            .ToListAsync(cancellationToken);

        // Switched-off accounts too: an old entry still needs its name.
        var accounts = await _db.Accounts
            .AsNoTracking()
            .Where(a => a.OrganizationId == access.OrganizationId)
            .ToListAsync(cancellationToken);

        var response = new List<ExpenseResponse>();

        foreach (var entry in entries)
        {
            JournalLine? debit = null;
            JournalLine? credit = null;

            foreach (var line in lines)
            {
                if (line.JournalEntryId != entry.Id)
                {
                    continue;
                }

                if (line.DebitMinor > 0)
                {
                    debit = line;
                }
                else
                {
                    credit = line;
                }
            }

            if (debit is null || credit is null)
            {
                continue;
            }

            Account? category = FindAccount(accounts, debit.AccountId);
            Account? money = FindAccount(accounts, credit.AccountId);
            string categoryName = category?.Name ?? "";

            response.Add(new ExpenseResponse
            {
                Id = entry.Id,
                ExpenseDate = entry.EntryDate,
                AccountId = debit.AccountId,
                CategoryName = categoryName,
                IsDrawings = entry.SourceType == SourceType.Drawings,
                AmountMinor = debit.DebitMinor,
                PaidFrom = money?.Code == MpesaCode ? PaymentMethod.Mpesa : PaymentMethod.Cash,

                // With no note, the description is the category name, so
                // that is not a note.
                Note = entry.Description == categoryName ? null : entry.Description
            });
        }

        return response;
    }

    // A shop cost, or Drawings. Never Sales, Inventory, Cash or any other
    // account, and never 5000: cost of goods sold is written by sales, from
    // what the stock cost, not typed in by hand.
    private static bool IsPayableHere(Account account)
    {
        if (account.Code == DrawingsCode && account.AccountClass == AccountClass.Equity)
        {
            return true;
        }

        return account.AccountClass == AccountClass.Expense && account.Code != CostOfGoodsSoldCode;
    }

    private static Account? FindAccount(List<Account> accounts, Guid id)
    {
        foreach (var account in accounts)
        {
            if (account.Id == id)
            {
                return account;
            }
        }

        return null;
    }

    private static ProblemDetails Rejected(string detail) => new()
    {
        Title = "The payment was rejected",
        Detail = detail,
        Status = StatusCodes.Status400BadRequest
    };
}
