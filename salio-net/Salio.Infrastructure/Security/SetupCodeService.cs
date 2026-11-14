using Microsoft.EntityFrameworkCore;
using Salio.Domain.Entities;

namespace Salio.Infrastructure.Security;

/// <summary>
/// The one place setup codes are made, checked, used and cancelled. The
/// command line and the admin page both call this; there is no second copy.
///
/// WHY THE CODE IS STORED IN PLAIN TEXT, NOT HASHED. Do not "harden" this.
/// A setup code is not a password. It is a short-lived, single-use token
/// that can only create one empty shop, and it dies after 7 days or on first
/// use. The developer needs to read it back to send it again. Guessing is
/// stopped by the "setup-redeem" rate limit (10 tries per address per hour),
/// not by hashing.
/// </summary>
public class SetupCodeService
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    private readonly SalioDbContext _db;

    public SetupCodeService(SalioDbContext db)
    {
        _db = db;
    }

    // A new code for one shop, valid for 7 days.
    public async Task<SetupCode> IssueAsync(string shopName, string email, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // 30^6 is about 729 million codes, so a clash is rare, but the
        // database's unique index would refuse one. Try again a few times.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            string code = SetupCodes.Generate();

            bool taken = await _db.SetupCodes.AnyAsync(c => c.Code == code, cancellationToken);

            if (taken)
            {
                continue;
            }

            var setupCode = new SetupCode
            {
                Id = Guid.NewGuid(),
                Code = code,
                ShopName = shopName.Trim(),
                Email = email.Trim(),
                CreatedAt = now,
                ExpiresAt = now + Lifetime
            };

            _db.SetupCodes.Add(setupCode);

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return setupCode;
            }
            catch (DbUpdateException)
            {
                // Someone else took the same code in the same moment. Remove
                // on a row never saved only forgets it; nothing is deleted.
                _db.SetupCodes.Remove(setupCode);
            }
        }

        throw new InvalidOperationException("Could not make a unique setup code. Try again.");
    }

    // The code if it can still be used, otherwise null. Null for unknown,
    // expired, revoked, already used, and badly typed alike: the caller shows
    // one message for all of them.
    public async Task<SetupCode?> FindRedeemableAsync(string? typed, DateTimeOffset now, CancellationToken cancellationToken)
    {
        string code = SetupCodes.Normalise(typed);

        if (!SetupCodes.IsWellFormed(code))
        {
            return null;
        }

        return await _db.SetupCodes
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Code == code
                && c.RedeemedAt == null
                && c.RevokedAt == null
                && c.ExpiresAt > now,
                cancellationToken);
    }

    // Marks the code used by this shop, only if it is still usable. Of two
    // people using one code at the same moment, exactly one gets true.
    // Run it inside the transaction that creates the shop.
    public async Task<bool> ClaimAsync(Guid setupCodeId, Guid organizationId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        int claimed = await _db.SetupCodes
            .Where(c => c.Id == setupCodeId
                && c.RedeemedAt == null
                && c.RevokedAt == null
                && c.ExpiresAt > now)
            .ExecuteUpdateAsync(set => set
                .SetProperty(c => c.RedeemedAt, now)
                .SetProperty(c => c.OrganizationId, organizationId),
                cancellationToken);

        return claimed == 1;
    }

    // Cancels a code that has not been used. False if it was already used,
    // already revoked, or does not exist.
    public async Task<bool> RevokeAsync(Guid setupCodeId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        int revoked = await _db.SetupCodes
            .Where(c => c.Id == setupCodeId
                && c.RedeemedAt == null
                && c.RevokedAt == null)
            .ExecuteUpdateAsync(set => set
                .SetProperty(c => c.RevokedAt, now),
                cancellationToken);

        return revoked == 1;
    }

    // Every code, newest first. For the admin page.
    public async Task<List<SetupCode>> ListAsync(CancellationToken cancellationToken)
    {
        return await _db.SetupCodes
            .AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
