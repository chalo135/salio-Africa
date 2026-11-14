using Microsoft.EntityFrameworkCore;
using Salio.Infrastructure.Security;

namespace Salio.Infrastructure;

// A SKU the server makes for a part added without one (decision D8): "Q-"
// and six characters from the setup-code alphabet, tried again on a clash.
// Used by quick-add and by "add as a new part" on a supplier invoice.
public static class ProductSkus
{
    // Null if ten tries all clashed (practically never).
    public static async Task<string?> GenerateFreeAsync(
        SalioDbContext db, Guid organizationId, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            string candidate = "Q-" + SetupCodes.Generate();

            bool taken = await db.Products.AnyAsync(
                p => p.OrganizationId == organizationId && p.Sku == candidate, cancellationToken);

            if (!taken)
            {
                return candidate;
            }
        }

        return null;
    }
}
