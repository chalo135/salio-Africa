using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Salio.Api.Contracts;
using Salio.Infrastructure;

namespace Salio.Api.Controllers;

/// <summary>
/// Read-only. This exists so the seeded account ids can be found without
/// opening pgAdmin: they are new Guids in every database.
/// </summary>
[ApiController]
[Route("api/accounts")]
public class AccountsController : ControllerBase
{
    private readonly SalioDbContext _db;

    public AccountsController(SalioDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<AccountResponse>>> GetAll(
        [FromQuery][BindRequired] Guid organizationId,
        CancellationToken cancellationToken)
    {
        var accounts = await _db.Accounts
            .AsNoTracking()
            .Where(a => a.OrganizationId == organizationId && a.IsActive)
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
}
