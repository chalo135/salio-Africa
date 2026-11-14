using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Salio.Api.Dtos;
using Salio.Api.Security;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Infrastructure;
using Salio.Infrastructure.Security;

namespace Salio.Api.Controllers;

/// <summary>
/// Who can sign in to this shop. Owner only, everything scoped to the shop
/// the guard returns.
///
/// One person, one account, however many shops (locked rule 10). So changes
/// here are made to the membership — this person's place in this shop — and
/// never reach the person's other shops. The exceptions are signing out and
/// switching off, which end their sessions everywhere: a cookie belongs to a
/// person, not a shop, so there is no way to end only one shop's.
/// </summary>
[ApiController]
[Authorize]
[Route("api/people")]
public class PeopleController : ControllerBase
{
    private readonly SalioDbContext _db;
    private readonly UserCreator _userCreator;
    private readonly OrganizationGuard _guard;

    public PeopleController(SalioDbContext db, UserCreator userCreator, OrganizationGuard guard)
    {
        _db = db;
        _userCreator = userCreator;
        _guard = guard;
    }

    [HttpGet]
    public async Task<ActionResult<List<PersonResponse>>> GetAll(
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

        OrganizationGuard.TryGetUserId(User, out Guid callerId);

        var rows = await (
            from membership in _db.Memberships.AsNoTracking()
            join user in _db.AppUsers.AsNoTracking()
                on membership.AppUserId equals user.Id
            where membership.OrganizationId == access.OrganizationId
            orderby user.FullName
            select new { membership, user })
            .ToListAsync(cancellationToken);

        var response = new List<PersonResponse>();

        foreach (var row in rows)
        {
            response.Add(ToResponse(row.membership, row.user, callerId));
        }

        return response;
    }

    [HttpPost]
    public async Task<ActionResult<PersonAddedResponse>> Add(
        CreatePersonRequest request,
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

        OrganizationGuard.TryGetUserId(User, out Guid callerId);

        string fullName = request.FullName.Trim();
        string email = request.Email.Trim();

        if (fullName == "" || !email.Contains('@'))
        {
            return BadRequest(Refused("Give the person's name and an email address."));
        }

        if (request.Role != MembershipRole.Owner && request.Role != MembershipRole.Cashier)
        {
            return BadRequest(Refused("The role must be Owner or Cashier."));
        }

        // Compared without case, the same way login finds an account.
        string emailLower = email.ToLower();

        var existing = await _db.AppUsers
            .FirstOrDefaultAsync(u => u.Email.ToLower() == emailLower, cancellationToken);

        // ---- Someone who already has a Salio account, here or elsewhere ----
        if (existing is not null)
        {
            var current = await _db.Memberships
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    m => m.OrganizationId == access.OrganizationId && m.AppUserId == existing.Id,
                    cancellationToken);

            if (current is not null)
            {
                return Conflict(new ProblemDetails
                {
                    Title = "Already in this shop",
                    Detail = current.DeactivatedAt is null
                        ? existing.FullName + " is already in this shop."
                        : existing.FullName + " is already in this shop, but switched off. Open them and switch them back on.",
                    Status = StatusCodes.Status409Conflict
                });
            }

            if (!existing.IsActive)
            {
                return BadRequest(Refused(
                    "The account for " + email + " has been switched off, so it cannot be added to a shop."));
            }

            // One person, one account: a place in this shop is added to the
            // account they already have. Their password does not change.
            var membership = new Membership
            {
                Id = Guid.NewGuid(),
                OrganizationId = access.OrganizationId,
                AppUserId = existing.Id,
                Role = request.Role,
                CreatedAt = DateTimeOffset.UtcNow
            };

            _db.Memberships.Add(membership);
            await _db.SaveChangesAsync(cancellationToken);

            return new PersonAddedResponse
            {
                Person = ToResponse(membership, existing, callerId),
                AddedExistingAccount = true,
                TemporaryPassword = null,
                Message = existing.FullName + " already has a Salio account, so this shop was added to it. "
                    + "They sign in with the password they already use."
            };
        }

        // ---- Someone new ----
        // The only moment this password exists in plain text. It is hashed
        // inside UserCreator, returned once below, and never stored.
        string temporaryPassword = TemporaryPassword.Generate();

        AppUser created;

        try
        {
            created = await _userCreator.CreateAsync(
                access.OrganizationId,
                fullName,
                email,
                temporaryPassword,
                request.Role,
                cancellationToken,
                mustChangePassword: true);
        }
        catch (InvalidOperationException ex)
        {
            // Someone took the email in the moment between the check above
            // and this save.
            return Conflict(new ProblemDetails
            {
                Title = "Not added",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }

        var newMembership = await _db.Memberships
            .AsNoTracking()
            .FirstAsync(
                m => m.OrganizationId == access.OrganizationId && m.AppUserId == created.Id,
                cancellationToken);

        return new PersonAddedResponse
        {
            Person = ToResponse(newMembership, created, callerId),
            AddedExistingAccount = false,
            TemporaryPassword = temporaryPassword,
            Message = created.FullName + " can sign in now with this temporary password, "
                + "and will be asked to choose their own."
        };
    }

    [HttpPut("{userId:guid}/role")]
    public async Task<ActionResult<PersonResponse>> ChangeRole(
        Guid userId,
        ChangeRoleRequest request,
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

        OrganizationGuard.TryGetUserId(User, out Guid callerId);

        if (request.Role != MembershipRole.Owner && request.Role != MembershipRole.Cashier)
        {
            return BadRequest(Refused("The role must be Owner or Cashier."));
        }

        var found = await FindAsync(access.OrganizationId, userId, cancellationToken);

        if (found is null)
        {
            return NotFound();
        }

        var (membership, user) = found.Value;

        bool losingOwnership = membership.Role == MembershipRole.Owner && request.Role != MembershipRole.Owner;

        // Guard 1: nobody removes their own ownership.
        if (losingOwnership && user.Id == callerId)
        {
            return BadRequest(Refused(
                "You cannot remove your own ownership. Another owner of this shop has to do it."));
        }

        // Guard 3: the shop must keep an owner who can sign in.
        if (losingOwnership && await IsLastActiveOwnerAsync(access.OrganizationId, membership, user, cancellationToken))
        {
            return BadRequest(Refused(
                user.FullName + " is the last owner of this shop. Make someone else an owner first: "
                + "a shop with no owner can never be looked after again."));
        }

        // Takes effect on their very next request: the shop and role are
        // looked up from memberships every time, never kept in the cookie.
        membership.Role = request.Role;
        await _db.SaveChangesAsync(cancellationToken);

        return ToResponse(membership, user, callerId);
    }

    [HttpPost("{userId:guid}/deactivate")]
    public async Task<ActionResult<PersonResponse>> Deactivate(
        Guid userId,
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

        OrganizationGuard.TryGetUserId(User, out Guid callerId);

        var found = await FindAsync(access.OrganizationId, userId, cancellationToken);

        if (found is null)
        {
            return NotFound();
        }

        var (membership, user) = found.Value;

        // Guard 2: nobody switches themselves off.
        if (user.Id == callerId)
        {
            return BadRequest(Refused(
                "You cannot switch yourself off. Another owner of this shop has to do it."));
        }

        if (membership.DeactivatedAt is not null)
        {
            return ToResponse(membership, user, callerId);
        }

        // Guard 3 again.
        if (membership.Role == MembershipRole.Owner
            && await IsLastActiveOwnerAsync(access.OrganizationId, membership, user, cancellationToken))
        {
            return BadRequest(Refused(
                user.FullName + " is the last owner of this shop. Make someone else an owner first: "
                + "a shop with no owner can never be looked after again."));
        }

        // Switched off in this shop only. Nothing is deleted: their sales
        // keep their name.
        membership.DeactivatedAt = DateTimeOffset.UtcNow;

        // And signed out now, not when the cookie runs out. Without a
        // membership the next request to this shop is refused anyway; a new
        // stamp also ends the session itself, on every device.
        user.SecurityStamp = Guid.NewGuid();

        await _db.SaveChangesAsync(cancellationToken);

        return ToResponse(membership, user, callerId);
    }

    [HttpPost("{userId:guid}/activate")]
    public async Task<ActionResult<PersonResponse>> Activate(
        Guid userId,
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

        OrganizationGuard.TryGetUserId(User, out Guid callerId);

        var found = await FindAsync(access.OrganizationId, userId, cancellationToken);

        if (found is null)
        {
            return NotFound();
        }

        var (membership, user) = found.Value;

        membership.DeactivatedAt = null;
        await _db.SaveChangesAsync(cancellationToken);

        return ToResponse(membership, user, callerId);
    }

    // Ends every session this person has, on every phone. Their password
    // does not change.
    [HttpPost("{userId:guid}/sign-out-all-devices")]
    public async Task<ActionResult<PersonResponse>> SignOutAllDevices(
        Guid userId,
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

        OrganizationGuard.TryGetUserId(User, out Guid callerId);

        var found = await FindAsync(access.OrganizationId, userId, cancellationToken);

        if (found is null)
        {
            return NotFound();
        }

        var (membership, user) = found.Value;

        user.SecurityStamp = Guid.NewGuid();
        await _db.SaveChangesAsync(cancellationToken);

        return ToResponse(membership, user, callerId);
    }

    // This person's place in this shop, and the person, both tracked so
    // they can be changed. Null if they have no place in this shop: someone
    // else's person is treated exactly like nobody.
    private async Task<(Membership, AppUser)?> FindAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var membership = await _db.Memberships
            .FirstOrDefaultAsync(
                m => m.OrganizationId == organizationId && m.AppUserId == userId,
                cancellationToken);

        if (membership is null)
        {
            return null;
        }

        var user = await _db.AppUsers.FirstAsync(u => u.Id == userId, cancellationToken);

        return (membership, user);
    }

    // True if this person is an owner who can sign in, and no other owner
    // of this shop can.
    private async Task<bool> IsLastActiveOwnerAsync(
        Guid organizationId,
        Membership membership,
        AppUser user,
        CancellationToken cancellationToken)
    {
        bool isActiveOwner = membership.Role == MembershipRole.Owner
            && membership.DeactivatedAt is null
            && user.IsActive;

        if (!isActiveOwner)
        {
            return false;
        }

        int otherActiveOwners = await (
            from m in _db.Memberships.AsNoTracking()
            join u in _db.AppUsers.AsNoTracking()
                on m.AppUserId equals u.Id
            where m.OrganizationId == organizationId
                && m.Role == MembershipRole.Owner
                && m.DeactivatedAt == null
                && u.IsActive
                && m.AppUserId != user.Id
            select m.Id)
            .CountAsync(cancellationToken);

        return otherActiveOwners == 0;
    }

    private static PersonResponse ToResponse(Membership membership, AppUser user, Guid callerId)
    {
        return new PersonResponse
        {
            UserId = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = membership.Role,
            IsActive = membership.DeactivatedAt is null && user.IsActive,
            MustChangePassword = user.MustChangePassword,
            IsYou = user.Id == callerId
        };
    }

    private static ProblemDetails Refused(string detail) => new()
    {
        Title = "Refused",
        Detail = detail,
        Status = StatusCodes.Status400BadRequest
    };
}
