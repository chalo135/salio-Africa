using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Salio.Api.Dtos;
using Salio.Api.Security;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;
using Salio.Infrastructure;

namespace Salio.Api.Controllers;

// The card on the Sell screen: what changed since the Owner last pressed
// Hide (Membership.ChangesSeenUntil), or in the last 7 days before the first
// Hide. Owner only: cost rises are money (locked rule 13).
[ApiController]
[Authorize]
[Route("api/welcome-card")]
public class WelcomeCardController : ControllerBase
{
    private const int MaxRows = 5;

    private readonly SalioDbContext _db;
    private readonly OrganizationGuard _guard;
    private readonly ImportedCostFinder _importedCosts;

    public WelcomeCardController(SalioDbContext db, OrganizationGuard guard, ImportedCostFinder importedCosts)
    {
        _db = db;
        _guard = guard;
        _importedCosts = importedCosts;
    }

    [HttpGet]
    public async Task<ActionResult<WelcomeCardResponse>> Get(
        [FromQuery] Guid? organizationId, CancellationToken cancellationToken)
    {
        var access = await _guard.ForAsync(User, organizationId, cancellationToken);

        if (!access.IsAllowed)
        {
            return OrganizationGuard.Deny(access);
        }

        // Asked here too, before anything is read, so a Cashier costs nothing.
        if (!access.IsOwner)
        {
            return OrganizationGuard.OwnerOnly();
        }

        if (!OrganizationGuard.TryGetUserId(User, out Guid viewerId))
        {
            return Unauthorized();
        }

        Guid shop = access.OrganizationId;

        DateTimeOffset? seenUntil = await _db.Memberships.AsNoTracking()
            .Where(m => m.OrganizationId == shop && m.AppUserId == viewerId)
            .Select(m => m.ChangesSeenUntil)
            .FirstOrDefaultAsync(cancellationToken);

        DateTimeOffset since = WelcomeCardRules.CardSince(seenUntil, DateTimeOffset.UtcNow);

        var products = await _db.Products.AsNoTracking()
            .Where(p => p.OrganizationId == shop)
            .ToListAsync(cancellationToken);

        var movements = await _db.StockMovements.AsNoTracking()
            .Where(m => m.OrganizationId == shop)
            .ToListAsync(cancellationToken);

        var importedMovementIds = await _importedCosts.LiveImportedMovementIdsAsync(shop, cancellationToken);

        var changes = await _db.PriceChanges.AsNoTracking()
            .Where(c => c.OrganizationId == shop && c.ChangedAt > since)
            .ToListAsync(cancellationToken);

        var userIds = changes
            .Where(c => c.ChangedByUserId != null)
            .Select(c => c.ChangedByUserId!.Value)
            .Distinct()
            .ToList();

        var names = await _db.AppUsers.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);

        WelcomeCardResponse? card = BuildCard(
            access.Role, viewerId, since, products, movements, importedMovementIds, changes, names);

        if (card is null)
        {
            return OrganizationGuard.OwnerOnly();
        }

        return card;
    }

    // Hide: the card shows nothing older than now. The only thing that moves
    // ChangesSeenUntil. Owner only, like the card.
    [HttpPost("hide")]
    public async Task<IActionResult> Hide(
        [FromQuery] Guid? organizationId, CancellationToken cancellationToken)
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

        if (!OrganizationGuard.TryGetUserId(User, out Guid viewerId))
        {
            return Unauthorized();
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        await _db.Memberships
            .Where(m => m.OrganizationId == access.OrganizationId && m.AppUserId == viewerId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ChangesSeenUntil, now), cancellationToken);

        return NoContent();
    }

    // The whole card, from what the shop holds. Static and database-free so
    // a test can drive it end to end (WelcomeCardFlowTests). Null for anyone
    // but an Owner: they get no card at all.
    //
    // products: every part in the shop, active or not (a price change may
    // name a retired part; only active parts get cost and ran-out rows).
    // priceChanges: the shop's price changes; older than `since` are skipped.
    // userNames: full names of the people who made them.
    [NonAction]
    public static WelcomeCardResponse? BuildCard(
        MembershipRole role,
        Guid viewerId,
        DateTimeOffset since,
        IReadOnlyList<Product> products,
        IReadOnlyList<StockMovement> movements,
        IReadOnlySet<Guid> importedMovementIds,
        IReadOnlyList<PriceChange> priceChanges,
        IReadOnlyDictionary<Guid, string> userNames)
    {
        if (role != MembershipRole.Owner)
        {
            return null;
        }

        var rows = new List<WelcomeCardRow>();

        foreach (var product in products.Where(p => p.IsActive))
        {
            var own = movements.Where(m => m.ProductId == product.Id).ToList();

            CostRise? rise = WelcomeCardRules.CostRoseSince(own, importedMovementIds, since);

            if (rise is not null)
            {
                rows.Add(new WelcomeCardRow
                {
                    Kind = "costUp",
                    ProductId = product.Id,
                    PartName = product.Name,
                    At = rise.At,
                    WasMinor = rise.WasMinor,
                    NowMinor = rise.NowMinor,
                    AskingMinor = product.SellPriceMinor,
                    KeptWasMinor = product.SellPriceMinor - rise.WasMinor,
                    KeptNowMinor = product.SellPriceMinor - rise.NowMinor
                });
            }

            DateTimeOffset? ranOutAt = WelcomeCardRules.RanOutSince(own, since);

            if (ranOutAt is not null)
            {
                rows.Add(new WelcomeCardRow
                {
                    Kind = "ranOut",
                    ProductId = product.Id,
                    PartName = product.Name,
                    At = ranOutAt.Value
                });
            }
        }

        // Price changes by someone else. A new part has no old price, so it
        // is not a change.
        foreach (PriceChange change in priceChanges)
        {
            if (change.ChangedAt <= since
                || change.OldPriceMinor is null
                || change.ChangedByUserId is not Guid changedBy
                || changedBy == viewerId)
            {
                continue;
            }

            rows.Add(new WelcomeCardRow
            {
                Kind = "priceChanged",
                ProductId = change.ProductId,
                PartName = products.FirstOrDefault(p => p.Id == change.ProductId)?.Name ?? "",
                At = change.ChangedAt,
                WasMinor = change.OldPriceMinor,
                NowMinor = change.NewPriceMinor,
                ChangedBy = FirstName(userNames.GetValueOrDefault(changedBy, ""))
            });
        }

        rows = rows.OrderByDescending(r => r.At).ToList();

        return new WelcomeCardResponse
        {
            Since = since,
            Total = rows.Count,
            ProductIds = rows.Select(r => r.ProductId).Distinct().ToList(),
            Rows = rows.Take(MaxRows).ToList()
        };
    }

    // "Peter Kamau" is "Peter". Someone with no name stored is "Someone".
    private static string FirstName(string fullName)
    {
        string trimmed = fullName.Trim();

        if (trimmed.Length == 0)
        {
            return "Someone";
        }

        int space = trimmed.IndexOf(' ');
        return space < 0 ? trimmed : trimmed.Substring(0, space);
    }
}
