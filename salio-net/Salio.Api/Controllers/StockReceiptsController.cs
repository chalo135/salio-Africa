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

// Goods arriving from a supplier: stock goes up, and the money goes out or
// is owed. Owner only, because it is money and stock value (locked rule 13).
[ApiController]
[Authorize]
[Route("api/stock-receipts")]
public class StockReceiptsController : ControllerBase
{
    private readonly SalioDbContext _db;
    private readonly StockReceiptService _receiptService;
    private readonly JournalPoster _journalPoster;
    private readonly StockPoster _stockPoster;
    private readonly OrganizationGuard _guard;

    public StockReceiptsController(
        SalioDbContext db,
        StockReceiptService receiptService,
        JournalPoster journalPoster,
        StockPoster stockPoster,
        OrganizationGuard guard)
    {
        _db = db;
        _receiptService = receiptService;
        _journalPoster = journalPoster;
        _stockPoster = stockPoster;
        _guard = guard;
    }

    [HttpPost]
    public async Task<ActionResult<StockReceiptResponse>> Create(
        CreateStockReceiptRequest request,
        CancellationToken cancellationToken)
    {
        // Rule 9: the shop is the one this user is a member of, never the id
        // as sent. Everything below uses access.OrganizationId.
        var access = await _guard.ForAsync(User, request.OrganizationId, cancellationToken);

        if (!access.IsAllowed)
        {
            return OrganizationGuard.Deny(access);
        }

        if (!access.IsOwner)
        {
            return OrganizationGuard.OwnerOnly();
        }

        // ---- Everything is checked before anything is written. ----

        if (request.Lines.Count == 0)
        {
            return BadRequest(Rejected("A stock receipt must have at least one line"));
        }

        if (request.PaidWith != PaymentMethod.Cash
            && request.PaidWith != PaymentMethod.Mpesa
            && request.PaidWith != PaymentMethod.Credit)
        {
            return BadRequest(Rejected("Paid with must be Cash, MPesa or Credit"));
        }

        foreach (var line in request.Lines)
        {
            if (line.Quantity <= 0)
            {
                return BadRequest(Rejected(
                    "Every quantity must be more than zero: " + line.ProductId));
            }

            // The database keeps three decimals. A finer quantity would be
            // rounded on save, and the stock would no longer match the cost
            // the journal booked for it.
            if (decimal.Round(line.Quantity, 3) != line.Quantity)
            {
                return BadRequest(Rejected(
                    "A quantity can have at most three decimal places: " + line.ProductId));
            }

            if (line.UnitCostMinor < 0)
            {
                return BadRequest(Rejected(
                    "A unit cost cannot be negative: " + line.ProductId));
            }
        }

        // Rule 9 again: a product id is only trusted once it is known to
        // belong to this shop. One query for all of them; an unknown id and
        // another shop's id get the same answer.
        var requestedIds = new List<Guid>();

        foreach (var line in request.Lines)
        {
            if (!requestedIds.Contains(line.ProductId))
            {
                requestedIds.Add(line.ProductId);
            }
        }

        var ourIds = await _db.Products
            .AsNoTracking()
            .Where(p => p.OrganizationId == access.OrganizationId && requestedIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);

        foreach (var id in requestedIds)
        {
            if (!ourIds.Contains(id))
            {
                return BadRequest(Rejected("That product is not in this shop: " + id));
            }
        }

        var accounts = await _db.Accounts
            .AsNoTracking()
            .Where(a => a.OrganizationId == access.OrganizationId && a.IsActive)
            .ToListAsync(cancellationToken);

        Guid cashAccountId = FindAccountId(accounts, "1000");
        Guid mpesaAccountId = FindAccountId(accounts, "1010");
        Guid inventoryAccountId = FindAccountId(accounts, "1200");
        Guid payableAccountId = FindAccountId(accounts, "2000");

        if (cashAccountId == Guid.Empty || mpesaAccountId == Guid.Empty
            || inventoryAccountId == Guid.Empty || payableAccountId == Guid.Empty)
        {
            return BadRequest(Rejected(
                "This organisation is missing one of the accounts a stock receipt needs: 1000, 1010, 1200, 2000"));
        }

        // ---- Build it. ----

        Guid receiptId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        string fromSupplier = string.IsNullOrWhiteSpace(request.SupplierName)
            ? ""
            : " from " + request.SupplierName.Trim();

        string note = "Stock receipt " + receiptId.ToString()[..8] + fromSupplier;

        var movements = new List<StockMovement>();

        foreach (var line in request.Lines)
        {
            // Goods arriving are a positive movement, at what they cost.
            movements.Add(new StockMovement
            {
                Id = Guid.NewGuid(),
                OrganizationId = access.OrganizationId,
                ProductId = line.ProductId,
                Type = StockMovementType.Receipt,
                QuantityChange = line.Quantity,
                UnitCostMinor = line.UnitCostMinor,
                OccurredAt = now,
                Note = note
            });
        }

        var journalEntry = new JournalEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = access.OrganizationId,
            EntryDate = request.ReceiptDate,
            Description = "Stock received" + fromSupplier,
            SourceType = SourceType.Purchase,
            SourceId = receiptId,
            CreatedAt = now
        };

        // One transaction around both halves, exactly as SalesController
        // does it. Each poster calls SaveChangesAsync, but inside this
        // transaction nothing is final until CommitAsync. If either poster
        // throws, the transaction is rolled back, or, for any other
        // exception, disposed without a commit, which also rolls it back.
        using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var journalLines = _receiptService.BuildJournalLines(
                access.OrganizationId, movements, request.PaidWith,
                cashAccountId, mpesaAccountId, payableAccountId, inventoryAccountId);

            // Journal rows go through JournalPoster, stock rows through
            // StockPoster, never through _db here.
            await _journalPoster.PostAsync(journalEntry, journalLines, cancellationToken);
            await _stockPoster.PostAsync(movements, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(Rejected(ex.Message));
        }

        return new StockReceiptResponse
        {
            Id = receiptId,
            ReceiptDate = request.ReceiptDate,
            TotalCostMinor = _receiptService.TotalCost(movements),
            LineCount = movements.Count,
            JournalEntryId = journalEntry.Id
        };
    }

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

    private static ProblemDetails Rejected(string detail) => new()
    {
        Title = "The stock receipt was rejected",
        Detail = detail,
        Status = StatusCodes.Status400BadRequest
    };
}
