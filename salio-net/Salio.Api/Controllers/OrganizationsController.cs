using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Salio.Api.Dtos;
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
    private readonly OrganizationGuard _guard;

    public OrganizationsController(SalioDbContext db, OrganizationGuard guard)
    {
        _db = db;
        _guard = guard;
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

    // The owner changes what the shop calls the things it sells, e.g.
    // "Parts" to "Spares". Setup chose it from the trade; this is the only
    // way to change it afterwards.
    [HttpPut("sellable-noun")]
    public async Task<IActionResult> UpdateSellableNoun(
        UpdateSellableNounRequest request,
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

        string sellableNoun = request.SellableNoun.Trim();

        if (sellableNoun == "" || sellableNoun.Length > 30)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "The word was not changed",
                Detail = "The word for what the shop sells must be 1 to 30 characters.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        await _db.Organizations
            .Where(o => o.Id == access.OrganizationId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(o => o.SellableNoun, sellableNoun),
                cancellationToken);

        return NoContent();
    }
}
