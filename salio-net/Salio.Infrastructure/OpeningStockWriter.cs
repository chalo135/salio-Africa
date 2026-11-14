using Microsoft.EntityFrameworkCore;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;

namespace Salio.Infrastructure;

public class OpeningStockResult
{
    // Null when it was posted. Otherwise the sentence to show, unchanged
    // from what the Opening stock screen has always said.
    public string? Error { get; set; }

    // True when the refusal is "this shop has already made a sale". An
    // import shows that sentence on its own, not against one row.
    public bool ShopHasTraded { get; set; }

    public Guid OpeningId { get; set; }
    public Guid JournalEntryId { get; set; }
    public long TotalCostMinor { get; set; }
    public int LineCount { get; set; }
}

// What a shop already owns on its shelves: stock goes up, debit 1200
// Inventory, credit 3000 Owner's Equity. Moved here unchanged from
// OpeningStockController so the import and quick-add use the same code.
// The caller has already checked who may do this (Owner only).
public class OpeningStockWriter
{
    private readonly SalioDbContext _db;
    private readonly StockReceiptService _receiptService;
    private readonly JournalPoster _journalPoster;
    private readonly StockPoster _stockPoster;

    public OpeningStockWriter(
        SalioDbContext db,
        StockReceiptService receiptService,
        JournalPoster journalPoster,
        StockPoster stockPoster)
    {
        _db = db;
        _receiptService = receiptService;
        _journalPoster = journalPoster;
        _stockPoster = stockPoster;
    }

    // sourceId: the journal entry's SourceId. Null means a fresh id, as the
    // Opening stock screen has always done. The import passes its
    // ImportBatchChange id (decision D4) so undo can find this entry.
    //
    // If the caller already has a transaction open, this joins it and leaves
    // committing (or rolling back on an Error) to the caller.
    public async Task<OpeningStockResult> WriteAsync(
        Guid organizationId,
        DateOnly openingDate,
        List<StockLineInput> lines,
        Guid? sourceId,
        CancellationToken cancellationToken)
    {
        // ---- Everything is checked before anything is written. ----

        // Opening stock is what was on the shelf BEFORE the first sale. Once
        // a sale has been costed, adding opening stock behind it would change
        // the average cost that sale was already booked at. Drafts do not
        // count: a draft never moved stock or reached the books.
        //
        // This is the one rule for every caller: the Opening stock screen,
        // the import and quick-add. Once a shop trades, new stock was bought,
        // which credits cash or a payable, never Owner's Equity.
        if (await ShopHasSoldAsync(organizationId, cancellationToken))
        {
            return new OpeningStockResult
            {
                ShopHasTraded = true,
                Error = "This shop has already made a sale, so opening stock can no longer be entered. "
                    + "Opening stock is what was on the shelves before the first sale. "
                    + "To add goods that arrive now, use Receive stock."
            };
        }

        if (lines.Count == 0)
        {
            return Failed("Add at least one line to the opening stock");
        }

        // Rule 9 again: a product id is only trusted once it is known to
        // belong to this shop. An unknown id and another shop's id get the
        // same answer.
        var requestedIds = new List<Guid>();

        foreach (var line in lines)
        {
            if (requestedIds.Contains(line.ProductId))
            {
                return Failed(
                    "That is on the list twice. Put its whole count on one line: "
                    + line.ProductId);
            }

            requestedIds.Add(line.ProductId);
        }

        var ourProducts = await _db.Products
            .AsNoTracking()
            .Where(p => p.OrganizationId == organizationId && requestedIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(cancellationToken);

        foreach (var line in lines)
        {
            var product = ourProducts.FirstOrDefault(p => p.Id == line.ProductId);

            if (product is null)
            {
                return Failed("Nothing in this shop matches: " + line.ProductId);
            }

            if (line.Quantity <= 0)
            {
                return Failed("How many are on the shelf must be more than zero: " + product.Name);
            }

            // The database keeps three decimals. A finer quantity would be
            // rounded on save, and the stock would no longer match the value
            // the journal booked for it.
            if (decimal.Round(line.Quantity, 3) != line.Quantity)
            {
                return Failed("A quantity can have at most three decimal places: " + product.Name);
            }

            // A cost of zero would leave the item worth nothing in the books
            // and drag its average cost down. Opening stock must say what it cost.
            if (line.UnitCostMinor <= 0)
            {
                return Failed("Enter what one unit cost the shop, more than zero: " + product.Name);
            }
        }

        var accounts = await _db.Accounts
            .AsNoTracking()
            .Where(a => a.OrganizationId == organizationId && a.IsActive)
            .ToListAsync(cancellationToken);

        Guid inventoryAccountId = FindAccountId(accounts, "1200");
        Guid equityAccountId = FindAccountId(accounts, "3000");

        if (inventoryAccountId == Guid.Empty || equityAccountId == Guid.Empty)
        {
            return Failed("This shop is missing one of the accounts opening stock needs: 1200, 3000");
        }

        // ---- Build it. ----

        Guid openingId = sourceId ?? Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // One date for the whole transaction: the movements tell the same
        // day as the journal entry below (see StockMovementDates).
        DateTimeOffset occurredAt = StockMovementDates.OccurredAtFor(openingDate, now);
        string note = "Opening stock " + openingId.ToString()[..8];

        var movements = new List<StockMovement>();

        foreach (var line in lines)
        {
            // A positive movement carrying its unit cost: that is what the
            // weighted average cost reads for stock coming in.
            movements.Add(new StockMovement
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                ProductId = line.ProductId,
                Type = StockMovementType.Adjustment,
                QuantityChange = line.Quantity,
                UnitCostMinor = line.UnitCostMinor,
                OccurredAt = occurredAt,
                Note = note
            });
        }

        // The same sum, rounded the same way, as a stock receipt, so the
        // books carry exactly the value the shelf is costed at.
        long totalCost;

        try
        {
            totalCost = _receiptService.TotalCost(movements);
        }
        catch (OverflowException)
        {
            return Failed("The total value is too large. Check the quantities and costs.");
        }

        var journalEntry = new JournalEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            EntryDate = openingDate,
            Description = "Opening stock",
            SourceType = SourceType.StockAdjustment,
            SourceId = openingId,
            CreatedAt = now
        };

        var journalLines = new List<JournalLine>
        {
            // The shelf is worth this much.
            NewLine(organizationId, inventoryAccountId, totalCost, 0),

            // And the owner put it there.
            NewLine(organizationId, equityAccountId, 0, totalCost)
        };

        // One transaction around both halves, as a stock receipt does it.
        // Nothing is final until CommitAsync; any failure rolls it all back.
        // A caller with a wider transaction (an import) owns it instead.
        bool ownTransaction = _db.Database.CurrentTransaction is null;
        using var transaction = ownTransaction
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            // Journal rows go through JournalPoster, stock rows through
            // StockPoster, never through _db here.
            await _journalPoster.PostAsync(journalEntry, journalLines, cancellationToken);
            await _stockPoster.PostAsync(movements, cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (InvalidOperationException ex)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken);
            }

            return Failed(ex.Message);
        }

        return new OpeningStockResult
        {
            OpeningId = openingId,
            JournalEntryId = journalEntry.Id,
            TotalCostMinor = totalCost,
            LineCount = movements.Count
        };
    }

    // The one definition of "this shop has traded": any sale that is not a
    // draft. The import preview and quick-add ask it too, so they can say in
    // advance what this writer would refuse.
    public async Task<bool> ShopHasSoldAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        return await _db.Sales
            .AnyAsync(s => s.OrganizationId == organizationId
                && s.Status != SaleStatus.Draft, cancellationToken);
    }

    private static OpeningStockResult Failed(string error) => new() { Error = error };

    private static Guid FindAccountId(List<Account> accounts, string code)
    {
        foreach (var account in accounts)
        {
            if (account.Code == code)
            {
                return account.Id;
            }
        }

        return Guid.Empty;
    }

    private static JournalLine NewLine(Guid organizationId, Guid accountId, long debit, long credit)
    {
        return new JournalLine
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            AccountId = accountId,
            DebitMinor = debit,
            CreditMinor = credit,
            Currency = "KES"
        };
    }
}
