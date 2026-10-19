using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Salio.Api.Dtos;
using Salio.Api.Security;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Infrastructure;

namespace Salio.Api.Controllers;

/// <summary>
/// The shop's chart of accounts. Listing it lets the seeded account ids be
/// found without opening pgAdmin. An owner may also add their own spending
/// categories, and switch off ones never used.
/// </summary>
[ApiController]
[Authorize]
[Route("api/accounts")]
public class AccountsController : ControllerBase
{
    private readonly SalioDbContext _db;
    private readonly OrganizationGuard _guard;

    public AccountsController(SalioDbContext db, OrganizationGuard guard)
    {
        _db = db;
        _guard = guard;
    }

    [HttpGet]
    public async Task<ActionResult<List<AccountResponse>>> GetAll(
        [FromQuery] Guid? organizationId,
        CancellationToken cancellationToken)
    {
        var access = await _guard.ForAsync(User, organizationId, cancellationToken);

        if (!access.IsAllowed)
        {
            return OrganizationGuard.Deny(access);
        }

        // The chart of accounts is the shape of the books: Owner only
        // (locked rule 13).
        if (!access.IsOwner)
        {
            return OrganizationGuard.OwnerOnly();
        }

        var accounts = await _db.Accounts
            .AsNoTracking()
            .Where(a => a.OrganizationId == access.OrganizationId && a.IsActive)
            .OrderBy(a => a.Code)
            .ToListAsync(cancellationToken);

        var response = new List<AccountResponse>();

        foreach (var account in accounts)
        {
            response.Add(new AccountResponse
            {
                Id = account.Id,
                Code = account.Code,
                Name = account.Name,
                AccountClass = account.AccountClass,
                IsActive = account.IsActive
            });
        }

        return response;
    }

    // An owner's own spending category. Expense accounts only: the code and
    // the class are Salio's choice, not the caller's.
    [HttpPost]
    public async Task<ActionResult<AccountResponse>> Create(
        CreateAccountRequest request,
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

        string name = request.Name.Trim();

        if (name == "")
        {
            return BadRequest(Rejected("Give the category a name"));
        }

        // There is one Drawings account, 3100, and it is equity. A second one
        // made here would be an expense, so the family's money would start
        // counting as a shop cost and lowering the profit.
        if (name.Contains("drawing", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(Rejected(
                "The shop already has a Drawings account. Money the family takes out "
                + "is recorded there, not as a shop cost, so a second one cannot be made."));
        }

        // Every account in the shop, switched off or not: names must stay
        // unique, and switched-off codes are still taken.
        var accounts = await _db.Accounts
            .AsNoTracking()
            .Where(a => a.OrganizationId == access.OrganizationId)
            .ToListAsync(cancellationToken);

        foreach (var account in accounts)
        {
            if (string.Equals(account.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(Rejected(account.IsActive
                    ? "This shop already has a category called " + account.Name
                    : "This shop already has a category called " + account.Name + ", switched off"));
            }
        }

        string? code = NextFreeExpenseCode(accounts);

        if (code is null)
        {
            return BadRequest(Rejected("There is no room for another category"));
        }

        var created = new Account
        {
            Id = Guid.NewGuid(),
            OrganizationId = access.OrganizationId,
            Code = code,
            Name = name,
            AccountClass = AccountClass.Expense,
            ParentId = null,
            IsActive = true
        };

        _db.Accounts.Add(created);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two owners adding a category at the same moment can pick the
            // same code; the unique index on (shop, code) stops the second.
            return Conflict(new ProblemDetails
            {
                Title = "Try again",
                Detail = "Another category was added at the same moment. Please try again.",
                Status = StatusCodes.Status409Conflict
            });
        }

        return new AccountResponse
        {
            Id = created.Id,
            Code = created.Code,
            Name = created.Name,
            AccountClass = created.AccountClass,
            IsActive = created.IsActive
        };
    }

    // Switching a category off, never deleting it. Only a spending category
    // that has never been used may go.
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Deactivate(
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

        // Tracked, not AsNoTracking: this row is about to change.
        var account = await _db.Accounts
            .FirstOrDefaultAsync(
                a => a.Id == id && a.OrganizationId == access.OrganizationId,
                cancellationToken);

        if (account is null)
        {
            return NotFound();
        }

        // Cash, Sales, Inventory and the rest are what sales and deliveries
        // post to; switching one off would stop the till. 5000 is written by
        // every sale. Only spending categories may be switched off.
        if (account.AccountClass != AccountClass.Expense || account.Code == "5000")
        {
            return BadRequest(Rejected(account.Name + " is part of how the shop works and cannot be switched off"));
        }

        // Reports are worked out from journal lines every time they are read.
        // An account with lines is part of the books, so it stays.
        bool hasJournalLines = await _db.JournalLines
            .AnyAsync(
                l => l.OrganizationId == access.OrganizationId && l.AccountId == account.Id,
                cancellationToken);

        if (hasJournalLines)
        {
            return BadRequest(Rejected(
                account.Name + " has payments recorded against it, so it stays in the books"));
        }

        account.Deactivate();
        await _db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    // The lowest free code above 5000 and below 5900. 5000 is cost of goods
    // sold and 5900 is Other, so neither can be given out.
    private static string? NextFreeExpenseCode(List<Account> accounts)
    {
        for (int number = 5001; number < 5900; number++)
        {
            string code = number.ToString();
            bool taken = false;

            foreach (var account in accounts)
            {
                if (account.Code == code)
                {
                    taken = true;
                }
            }

            if (!taken)
            {
                return code;
            }
        }

        return null;
    }

    private static ProblemDetails Rejected(string detail) => new()
    {
        Title = "Refused",
        Detail = detail,
        Status = StatusCodes.Status400BadRequest
    };
}
