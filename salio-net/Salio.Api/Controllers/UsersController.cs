using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Salio.Api.Dtos;
using Salio.Api.Security;
using Salio.Infrastructure;
using Salio.Infrastructure.Security;

namespace Salio.Api.Controllers;

/// <summary>
/// An Owner managing the people in their own shop (locked rule 14: an Owner
/// creates their own Cashiers). Every action is scoped to the shop the caller
/// is an Owner of, never to a shop id they simply named.
/// </summary>
[ApiController]
[Authorize]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly SalioDbContext _db;
    private readonly UserCreator _userCreator;
    private readonly OrganizationGuard _guard;

    public UsersController(SalioDbContext db, UserCreator userCreator, OrganizationGuard guard)
    {
        _db = db;
        _userCreator = userCreator;
        _guard = guard;
    }

    [HttpPost]
    public async Task<ActionResult<CreatedUserResponse>> Create(
        CreateUserRequest request,
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

        try
        {
            var created = await _userCreator.CreateAsync(
                access.OrganizationId,
                request.FullName,
                request.Email,
                request.Password,
                request.Role,
                cancellationToken);

            return new CreatedUserResponse
            {
                Id = created.Id,
                FullName = created.FullName,
                Email = created.Email,
                Role = request.Role,
                OrganizationId = access.OrganizationId
            };
        }
        catch (InvalidOperationException ex)
        {
            // A short password, a taken email. The caller needs to read why.
            return BadRequest(new ProblemDetails
            {
                Title = "The user was rejected",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
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
