using Microsoft.EntityFrameworkCore;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;

namespace Salio.Infrastructure;

public class StockReceiptResult
{
    // Null when it was posted. Otherwise the sentence to show, unchanged
    // from what the Receive stock screen has always said.
    public string? Error { get; set; }

    // Set when the refusal was a closed period (a filed tax return covers
    // the receipt date): that date and which return. Null otherwise.
    public DateOnly? ClosedPeriodDate { get; set; }
    public TaxType? ClosedPeriodTaxType { get; set; }

    public Guid ReceiptId { get; set; }
    public Guid JournalEntryId { get; set; }
    public long TotalCostMinor { get; set; }
    public int LineCount { get; set; }
}

// Goods arriving from a supplier: stock goes up, and the money goes out
// (1000 Cash, 1010 M-Pesa) or is owed (2000 Accounts Payable). Moved here
// unchanged from StockReceiptsController so the invoice reader uses the same
// code. The caller has already checked who may do this (Owner only).
public class StockReceiptWriter
{
    private readonly SalioDbContext _db;
    private readonly StockReceiptService _receiptService;
    private readonly JournalPoster _journalPoster;
    private readonly StockPoster _stockPoster;

    public StockReceiptWriter(
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

    // If the caller already has a transaction open, this joins it and leaves
    // committing (or rolling back on an Error) to the caller.
    //
    // receiptId: the journal entry's SourceId. Null means a fresh id, as the
    // Receive stock screen has always done. The import passes its batch id
    // so that undo can find this one receipt again.
    public async Task<StockReceiptResult> WriteAsync(
        Guid organizationId,
        DateOnly receiptDate,
        PaymentMethod paidWith,
        string? supplierName,
        List<StockLineInput> lines,
        CancellationToken cancellationToken,
        Guid? receiptId = null)
    {
        // ---- Everything is checked before anything is written. ----

        if (lines.Count == 0)
        {
            return Failed("A stock receipt must have at least one line");
        }

        if (paidWith != PaymentMethod.Cash
            && paidWith != PaymentMethod.Mpesa
            && paidWith != PaymentMethod.Credit)
        {
            return Failed("Paid with must be Cash, MPesa or Credit");
        }

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                return Failed("Every quantity must be more than zero: " + line.ProductId);
            }

            // The database keeps three decimals. A finer quantity would be
            // rounded on save, and the stock would no longer match the cost
            // the journal booked for it.
            if (decimal.Round(line.Quantity, 3) != line.Quantity)
            {
                return Failed("A quantity can have at most three decimal places: " + line.ProductId);
            }

            if (line.UnitCostMinor < 0)
            {
                return Failed("A unit cost cannot be negative: " + line.ProductId);
            }
        }

        // Rule 9 again: a product id is only trusted once it is known to
        // belong to this shop. One query for all of them; an unknown id and
        // another shop's id get the same answer.
        var requestedIds = new List<Guid>();

        foreach (var line in lines)
        {
            if (!requestedIds.Contains(line.ProductId))
            {
                requestedIds.Add(line.ProductId);
            }
        }

        var ourIds = await _db.Products
            .AsNoTracking()
            .Where(p => p.OrganizationId == organizationId && requestedIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in requestedIds)
        {
            if (!ourIds.Contains(id))
            {
                return Failed("Nothing in this shop matches: " + id);
            }
        }

        var accounts = await _db.Accounts
            .AsNoTracking()
            .Where(a => a.OrganizationId == organizationId && a.IsActive)
            .ToListAsync(cancellationToken);

        Guid cashAccountId = FindAccountId(accounts, "1000");
        Guid mpesaAccountId = FindAccountId(accounts, "1010");
        Guid inventoryAccountId = FindAccountId(accounts, "1200");
        Guid payableAccountId = FindAccountId(accounts, "2000");

        if (cashAccountId == Guid.Empty || mpesaAccountId == Guid.Empty
            || inventoryAccountId == Guid.Empty || payableAccountId == Guid.Empty)
        {
            return Failed(
                "This organisation is missing one of the accounts a stock receipt needs: 1000, 1010, 1200, 2000");
        }

        // ---- Build it. ----

        Guid theReceiptId = receiptId ?? Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // One date for the whole transaction. The journal entry is dated
        // receiptDate (an invoice's own date, say 07 Nov); the movements used
        // to be stamped "now" (09 Nov). Now they tell the same day.
        DateTimeOffset occurredAt = StockMovementDates.OccurredAtFor(receiptDate, now);

        string fromSupplier = string.IsNullOrWhiteSpace(supplierName)
            ? ""
            : " from " + supplierName.Trim();

        string note = NoteFor(theReceiptId, supplierName);

        var movements = new List<StockMovement>();

        foreach (var line in lines)
        {
            // Goods arriving are a positive movement, at what they cost.
            movements.Add(new StockMovement
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                ProductId = line.ProductId,
                Type = StockMovementType.Receipt,
                QuantityChange = line.Quantity,
                UnitCostMinor = line.UnitCostMinor,
                OccurredAt = occurredAt,
                Note = note
            });
        }

        var journalEntry = new JournalEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            EntryDate = receiptDate,
            Description = "Stock received" + fromSupplier,
            SourceType = SourceType.Purchase,
            SourceId = theReceiptId,
            CreatedAt = now
        };

        // One transaction around both halves, exactly as SalesController
        // does it. Each poster calls SaveChangesAsync, but inside this
        // transaction nothing is final until CommitAsync. If either poster
        // throws, the transaction is rolled back, or, for any other
        // exception, disposed without a commit, which also rolls it back.
        // A caller with a wider transaction (an invoice commit) owns it instead.
        bool ownTransaction = _db.Database.CurrentTransaction is null;
        using var transaction = ownTransaction
            ? await _db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        try
        {
            var journalLines = _receiptService.BuildJournalLines(
                organizationId, movements, paidWith,
                cashAccountId, mpesaAccountId, payableAccountId, inventoryAccountId);

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

            var failed = Failed(ex.Message);

            // JournalPoster refused because a tax return covers this date.
            // Said separately so the invoice screen can offer "post today".
            if (ex is ClosedPeriodException closed)
            {
                failed.ClosedPeriodDate = closed.EntryDate;
                failed.ClosedPeriodTaxType = closed.TaxType;
            }

            return failed;
        }

        return new StockReceiptResult
        {
            ReceiptId = theReceiptId,
            JournalEntryId = journalEntry.Id,
            TotalCostMinor = _receiptService.TotalCost(movements),
            LineCount = movements.Count
        };
    }

    // The note on every movement of one receipt. Undo of an import reads it
    // to find exactly that receipt's movements, so it lives in one place.
    public static string NoteFor(Guid receiptId, string? supplierName)
    {
        string fromSupplier = string.IsNullOrWhiteSpace(supplierName)
            ? ""
            : " from " + supplierName.Trim();

        return "Stock receipt " + receiptId.ToString()[..8] + fromSupplier;
    }

    private static StockReceiptResult Failed(string error) => new() { Error = error };

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
}
