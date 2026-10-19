using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Salio.Api.Security;
using Salio.Domain.Entities;
using Salio.Infrastructure;

namespace Salio.Api.Controllers;

[ApiController]
[Authorize]
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
        // Only the shops this person belongs to. Returning every
        // organisation in the database would hand one shopkeeper the
        // customer list of every other (locked rules 9 and 10).
        if (!OrganizationGuard.TryGetUserId(User, out Guid userId))
        {
            return Unauthorized();
        }

        return await (
            from membership in _db.Memberships.AsNoTracking()
            join organization in _db.Organizations.AsNoTracking()
                on membership.OrganizationId equals organization.Id
            where membership.AppUserId == userId
            orderby organization.Name
            select organization)
            .ToListAsync(cancellationToken);
    }
}
