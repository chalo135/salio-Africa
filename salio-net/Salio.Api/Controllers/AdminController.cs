using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Salio.Api.Dtos;
using Salio.Api.Security;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Infrastructure;
using Salio.Infrastructure.Security;

namespace Salio.Api.Controllers;

/// <summary>
/// The platform admin's area: issue setup codes, list them, revoke one, and
/// list shops. That is all it does. It never shows a shop's sales figures,
/// books, stock, tax or customers, never writes inside a shop, and has no
/// way to sign in as anyone else.
///
/// Deliberately NOT [Authorize]: that would answer a stranger 401 and admit
/// the area exists. PlatformAdminFilter answers everyone who is not the
/// admin with the same bare 404 as an unknown path. The "admin" rate limit
/// does the same when it trips (Program.cs).
///
/// Issuing and revoking a code each write one AdminAuditLog row, in the same
/// transaction as the change. Looking at a list writes nothing.
/// </summary>
[ApiController]
[Route("api/admin")]
[ServiceFilter(typeof(PlatformAdminFilter))]
[EnableRateLimiting("admin")]
public class AdminController : ControllerBase
{
    private readonly SalioDbContext _db;
    private readonly SetupCodeService _setupCodes;
    private readonly IConfiguration _configuration;

    public AdminController(SalioDbContext db, SetupCodeService setupCodes, IConfiguration configuration)
    {
        _db = db;
        _setupCodes = setupCodes;
        _configuration = configuration;
    }

    // Lets the web page know it may draw the admin screen. Everyone else
    // gets the filter's 404.
    [HttpGet("session")]
    public IActionResult Session()
    {
        return NoContent();
    }

    [HttpPost("setup-codes")]
    public async Task<ActionResult<IssuedSetupCodeResponse>> IssueSetupCode(
        IssueSetupCodeRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ShopName))
        {
            return BadRequest(Refused("Type the shop name."));
        }

        // The code and its audit row are saved together or not at all.
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var setupCode = await _setupCodes.IssueAsync(request.ShopName, request.Email, cancellationToken);

        _db.AdminAuditLogs.Add(new AdminAuditLog
        {
            Id = Guid.NewGuid(),
            AdminUserId = CurrentUserId(),
            Action = "IssueSetupCode",
            TargetSetupCodeId = setupCode.Id,
            At = DateTimeOffset.UtcNow,
            Detail = Shorten("Issued for " + setupCode.ShopName + " <" + setupCode.Email + ">")
        });

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        string code = SetupCodes.Format(setupCode.Code);

        // From PublicBaseUrl. If that was never set on this server, the
        // address this page was opened on, which is the till's own address.
        string link = SetupLink.Build(
            _configuration, setupCode.Code, Request.Scheme + "://" + Request.Host);

        // The expiry date as a Kenyan reads it: East Africa Time is UTC+3
        // all year, with no daylight saving.
        string expires = setupCode.ExpiresAt
            .ToOffset(TimeSpan.FromHours(3))
            .ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

        string message =
            "Hello! Here is your Salio setup link for " + setupCode.ShopName + ": " + link + "\n"
            + "If it asks for a code, type " + code + ". It works once and expires on " + expires + ".";

        return new IssuedSetupCodeResponse
        {
            Id = setupCode.Id,
            Code = code,
            Link = link,
            ExpiresAt = setupCode.ExpiresAt,
            Message = message
        };
    }

    // Every code, newest first. Not an admin action: nothing is logged.
    [HttpGet("setup-codes")]
    public async Task<ActionResult<List<AdminSetupCodeResponse>>> ListSetupCodes(CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var codes = await _setupCodes.ListAsync(cancellationToken);

        return codes.Select(c => new AdminSetupCodeResponse
        {
            Id = c.Id,
            Code = SetupCodes.Format(c.Code),
            ShopName = c.ShopName,
            Email = c.Email,
            IssuedAt = c.CreatedAt,
            ExpiresAt = c.ExpiresAt,
            RedeemedAt = c.RedeemedAt,
            RevokedAt = c.RevokedAt,
            Status = StatusOf(c, now),
            OrganizationId = c.OrganizationId
        }).ToList();
    }

    [HttpPost("setup-codes/{id:guid}/revoke")]
    public async Task<IActionResult> RevokeSetupCode(Guid id, CancellationToken cancellationToken)
    {
        var setupCode = await _db.SetupCodes
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (setupCode is null)
        {
            return NotFound(Refused("There is no setup code with that id."));
        }

        if (setupCode.RedeemedAt is not null)
        {
            return Conflict(Refused("That code has already been used to set up a shop, so it cannot be revoked."));
        }

        // Already revoked: nothing to do, and nothing done, so no audit row.
        if (setupCode.RevokedAt is not null)
        {
            return NoContent();
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        bool revoked = await _setupCodes.RevokeAsync(id, now, cancellationToken);

        if (!revoked)
        {
            // Used or revoked by someone else a moment ago.
            return Conflict(Refused("That code was used or revoked a moment ago. Refresh the list."));
        }

        _db.AdminAuditLogs.Add(new AdminAuditLog
        {
            Id = Guid.NewGuid(),
            AdminUserId = CurrentUserId(),
            Action = "RevokeSetupCode",
            TargetSetupCodeId = id,
            At = now,
            Detail = Shorten("Revoked " + SetupCodes.Format(setupCode.Code) + " for " + setupCode.ShopName)
        });

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return NoContent();
    }

    // Every shop, oldest first. Read only, nothing logged. The last sale's
    // time is the only thing read from a shop's sales (decision D8): no
    // amounts, no counts, no customers.
    [HttpGet("shops")]
    public async Task<ActionResult<List<AdminShopResponse>>> ListShops(CancellationToken cancellationToken)
    {
        return await _db.Organizations
            .AsNoTracking()
            .OrderBy(o => o.CreatedAt)
            .Select(o => new AdminShopResponse
            {
                OrganizationId = o.Id,
                Name = o.Name,
                OwnerEmail = _db.Memberships
                    .Where(m => m.OrganizationId == o.Id
                        && m.Role == MembershipRole.Owner
                        && m.DeactivatedAt == null)
                    .OrderBy(m => m.CreatedAt)
                    .Select(m => _db.AppUsers
                        .Where(u => u.Id == m.AppUserId)
                        .Select(u => u.Email)
                        .FirstOrDefault())
                    .FirstOrDefault(),
                CreatedAt = o.CreatedAt,
                LastSaleAt = _db.Sales
                    .Where(s => s.OrganizationId == o.Id)
                    .Max(s => (DateTimeOffset?)s.OccurredAt)
            })
            .ToListAsync(cancellationToken);
    }

    // Any other path or method under /api/admin lands here, so it gets the
    // same bare 404 as an unknown path, from the filter or from this. Without
    // it, a wrong method on a real path would answer 405 and give the area
    // away.
    [Route("{**rest}")]
    public IActionResult Anything()
    {
        return new BareNotFoundResult();
    }

    private static string StatusOf(SetupCode code, DateTimeOffset now)
    {
        if (code.RedeemedAt is not null)
        {
            return "Redeemed";
        }

        if (code.RevokedAt is not null)
        {
            return "Revoked";
        }

        if (code.ExpiresAt <= now)
        {
            return "Expired";
        }

        return "Unused";
    }

    // The filter has already proved this id belongs to the admin.
    private Guid CurrentUserId()
    {
        OrganizationGuard.TryGetUserId(User, out Guid userId);
        return userId;
    }

    // The Detail column holds at most 500 characters.
    private static string Shorten(string text)
    {
        return text.Length <= 500 ? text : text[..500];
    }

    private static ProblemDetails Refused(string detail) => new()
    {
        Title = "Refused",
        Detail = detail
    };
}
