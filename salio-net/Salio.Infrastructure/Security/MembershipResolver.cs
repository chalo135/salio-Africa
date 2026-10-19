using Microsoft.EntityFrameworkCore;
using Salio.Domain.Services;

namespace Salio.Infrastructure.Security;

/// <summary>
/// Loads a user's memberships and asks MembershipRules whether the request
/// may go ahead (locked rule 9). This is the only place that answers
/// "which organisation may this request touch?".
/// </summary>
public class MembershipResolver
{
    private readonly SalioDbContext _db;
    private readonly MembershipRules _rules;

    public MembershipResolver(SalioDbContext db, MembershipRules rules)
    {
        _db = db;
        _rules = rules;
    }

    public async Task<MembershipAccessResult> ResolveAsync(
        Guid userId,
        Guid? requestedOrganizationId,
        CancellationToken cancellationToken)
    {
        var memberships = await _db.Memberships
            .AsNoTracking()
            .Where(m => m.AppUserId == userId)
            .ToListAsync(cancellationToken);

        // The decision itself is in Domain, where it is tested without a
        // database. Nothing here interprets the rules.
        return _rules.Resolve(memberships, requestedOrganizationId);
    }
}
