using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Salio.Domain.Entities;
using Salio.Infrastructure;

namespace Salio.Api.Controllers;

[ApiController]
[Route("api/organizations")]
public class OrganizationsController : ControllerBase
{
    private readonly SalioDbContext _db;

    public OrganizationsController(SalioDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<Organization>>> GetAll(CancellationToken cancellationToken)
    {
        return await _db.Organizations
            .AsNoTracking()
            .OrderBy(o => o.Name)
            .ToListAsync(cancellationToken);
    }
}
