using Salio.Domain.Entities;

namespace Salio.Infrastructure;

/// <summary>
/// The only place in the application that writes stock movements.
/// It adds rows and nothing else: there is no update method and no delete
/// method here, because a stock movement is never changed once written.
/// A mistake is corrected by posting a new movement in the opposite
/// direction, which leaves the original visible in the history.
/// </summary>
public class StockPoster
{
    private readonly SalioDbContext _db;

    public StockPoster(SalioDbContext db)
    {
        _db = db;
    }

    public async Task PostAsync(
        List<StockMovement> movements,
        CancellationToken cancellationToken)
    {
        if (movements.Count == 0)
        {
            throw new InvalidOperationException(
                "There must be at least one stock movement to post");
        }

        // Add, never Update. EF only writes INSERT statements for entities in
        // the Added state, so nothing here can overwrite an existing row.
        _db.StockMovements.AddRange(movements);

        // One SaveChangesAsync is one database transaction. A sale that moves
        // three products lands as three rows together, or as none of them.
        await _db.SaveChangesAsync(cancellationToken);
    }
}
