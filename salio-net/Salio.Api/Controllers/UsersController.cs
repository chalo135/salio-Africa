using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Salio.Api.Dtos;
using Salio.Api.Security;
using Salio.Infrastructure;

namespace Salio.Api.Controllers;

/// <summary>
/// Shop-wide sign-out. Adding people is PeopleController's job (a temporary
/// password the person must change); the old POST /api/users, where an Owner
/// chose someone's permanent password, was removed on 2026-11-07. Every
/// action is scoped to the shop the caller is an Owner of, never to a shop id
/// they simply named.
/// </summary>
[ApiController]
[Authorize]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly SalioDbContext _db;
    private readonly OrganizationGuard _guard;

    public UsersController(SalioDbContext db, OrganizationGuard guard)
    {
        _db = db;
        _guard = guard;
    }

    /// <summary>
    /// Locked rule 12: signs out every device in this shop by giving every
    /// member a new security stamp. Cookies holding the old stamp die on
    /// their next request. No password changes.
    ///
    /// This includes the Owner calling it: a lost phone may well be theirs,
    /// so they are signed out too and must log in again.
    /// </summary>
    [HttpPost("sign-out-all-devices")]
    public async Task<ActionResult<SignedOutDevicesResponse>> SignOutAllDevices(
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

        var memberUserIds = await _db.Memberships
            .AsNoTracking()
            .Where(m => m.OrganizationId == access.OrganizationId)
            .Select(m => m.AppUserId)
            .ToListAsync(cancellationToken);

        // Tracked on purpose: these rows are being changed.
        var users = await _db.AppUsers
            .Where(u => memberUserIds.Contains(u.Id))
            .ToListAsync(cancellationToken);

        foreach (var user in users)
        {
            user.SecurityStamp = Guid.NewGuid();
        }

        await _db.SaveChangesAsync(cancellationToken);

        return new SignedOutDevicesResponse { AccountsSignedOut = users.Count };
    }
}
