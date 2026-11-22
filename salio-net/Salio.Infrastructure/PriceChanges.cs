using Salio.Domain.Entities;

namespace Salio.Infrastructure;

// The one way a selling-price change is recorded. Every place that sets
// Product.SellPriceMinor calls this next to it (PriceChangeGuardTests checks
// that no file sets the price without it). It only adds the row; the
// caller's own SaveChanges writes it, in the same save as the price.
public static class PriceChanges
{
    // oldPriceMinor: null for a part being created. Nothing is written when
    // the price did not actually change.
    public static void Record(
        SalioDbContext db, Product product, long? oldPriceMinor, Guid? changedByUserId)
    {
        if (oldPriceMinor == product.SellPriceMinor)
        {
            return;
        }

        db.PriceChanges.Add(new PriceChange
        {
            Id = Guid.NewGuid(),
            OrganizationId = product.OrganizationId,
            ProductId = product.Id,
            OldPriceMinor = oldPriceMinor,
            NewPriceMinor = product.SellPriceMinor,
            ChangedByUserId = changedByUserId,
            ChangedAt = DateTimeOffset.UtcNow
        });
    }
}
