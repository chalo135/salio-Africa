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

// Counting the stock. The owner says what is really on the shelf; where that
// differs from what the stock movements say, Salio writes one adjustment
// movement per product and one journal entry for the whole count:
// stock missing is a cost (debit 5700 Stock Loss, credit 1200 Inventory),
// stock found is the reverse. Owner only: it is stock value (locked rule 13).
[ApiController]
[Authorize]
[Route("api/stock-counts")]
public class StockCountsController : ControllerBase
{
    private const string InventoryCode = "1200";
    private const string StockLossCode = "5700";

    private readonly SalioDbContext _db;
    private readonly StockService _stockService;
    private readonly JournalPoster _journalPoster;
    private readonly StockPoster _stockPoster;
    private readonly OrganizationGuard _guard;

    public StockCountsController(
        SalioDbContext db,
        StockService stockService,
        JournalPoster journalPoster,
        StockPoster stockPoster,
        OrganizationGuard guard)
    {
        _db = db;
        _stockService = stockService;
        _journalPoster = journalPoster;
        _stockPoster = stockPoster;
        _guard = guard;
    }

    [HttpPost]
    public async Task<ActionResult<StockCountResponse>> Create(
        CreateStockCountRequest request,
        CancellationToken cancellationToken)
    {
        // Rule 9: everything below uses access.OrganizationId, never the id
        // as sent.
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
            return BadRequest(Rejected("Add at least one line to the count"));
        }

        var requestedIds = new List<Guid>();

        foreach (var line in request.Lines)
        {
            if (requestedIds.Contains(line.ProductId))
            {
                return BadRequest(Rejected(
                    "That is on the count twice. Put its whole count on one line: "
                    + line.ProductId));
            }

            requestedIds.Add(line.ProductId);
        }

        // Rule 9 again: a product id is only trusted once it is known to
        // belong to this shop. An unknown id and another shop's id get the
        // same answer.
        var ourProducts = await _db.Products
            .AsNoTracking()
            .Where(p => p.OrganizationId == access.OrganizationId && requestedIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name })
            .ToListAsync(cancellationToken);

        foreach (var line in request.Lines)
        {
            var product = ourProducts.FirstOrDefault(p => p.Id == line.ProductId);

            if (product is null)
            {
                return BadRequest(Rejected("Nothing in this shop matches: " + line.ProductId));
            }

            if (line.CountedQuantity < 0)
            {
                return BadRequest(Rejected(
                    "A counted quantity cannot be below zero. Enter 0 for an empty shelf: "
                    + product.Name));
            }

            // The database keeps three decimals. A finer quantity would be
            // rounded on save, and the stock would no longer match the value
            // the journal booked for it.
            if (decimal.Round(line.CountedQuantity, 3) != line.CountedQuantity)
            {
                return BadRequest(Rejected(
                    "A quantity can have at most three decimal places: " + product.Name));
            }
        }

        // Every movement of the counted products, in one query, then sorted
        // out per product in memory.
        var allMovements = await _db.StockMovements
            .AsNoTracking()
            .Where(m => m.OrganizationId == access.OrganizationId && requestedIds.Contains(m.ProductId))
            .ToListAsync(cancellationToken);

        Guid countId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string note = "Stock count " + countId.ToString()[..8];

        var movements = new List<StockMovement>();
        var resultLines = new List<StockCountLineResponse>();
        long missingValue = 0;
        long foundValue = 0;

        foreach (var line in request.Lines)
        {
            var product = ourProducts.First(p => p.Id == line.ProductId);

            var productMovements = new List<StockMovement>();

            foreach (var movement in allMovements)
            {
                if (movement.ProductId == line.ProductId)
                {
                    productMovements.Add(movement);
                }
            }

            decimal expected = _stockService.CurrentQuantity(productMovements);

            // PLACEHOLDER, pending Chalo's decision. A product below zero has
            // no meaningful average cost (locked rule 16), so there is no
            // honest value for its difference. Until Chalo decides what a
            // count of such a product should do, the whole count is refused
            // and nothing is written.
            if (expected < 0)
            {
                return BadRequest(Rejected(
                    product.Name + " stands below zero in Salio (" + expected
                    + "), so it has no cost to value the count at. Receive stock for it first, "
                    + "or leave it out of this count. Nothing was saved."));
            }

            decimal difference = line.CountedQuantity - expected;
            long unitCost = 0;
            long value = 0;

            if (difference != 0)
            {
                // Read-only use of the owner-written StockService.
                unitCost = _stockService.CalculateWeightedAverageCost(productMovements);

                // PLACEHOLDER, pending Chalo's decision. Stock found for a
                // product with no cost on record (never received at a price,
                // or sold down to zero) would have to be valued at 0, which
                // is inventing a figure. Refused for now; nothing is written.
                if (difference > 0 && unitCost <= 0)
                {
                    return BadRequest(Rejected(
                        "The count found " + difference + " more of " + product.Name
                        + " than Salio shows, but Salio has no cost on record for it. "
                        + "Receive it with Stock in, at what it cost, instead. Nothing was saved."));
                }

                // Value = difference x average cost, rounded once per line.
                // The journal is built from these same rounded values, so it
                // equals their sum exactly.
                try
                {
                    value = checked((long)Math.Round(
                        Math.Abs(difference) * unitCost, MidpointRounding.AwayFromZero));
                }
                catch (OverflowException)
                {
                    return BadRequest(Rejected(
                        "The value of the difference is too large. Check the count: " + product.Name));
                }

                if (difference < 0)
                {
                    missingValue = missingValue + value;
                }
                else
                {
                    foundValue = foundValue + value;
                }

                // Counted minus expected: negative takes stock off, positive
                // puts it back. It carries the average cost, so stock found
                // comes back in at the price the shelf is already valued at.
                movements.Add(new StockMovement
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = access.OrganizationId,
                    ProductId = line.ProductId,
                    Type = StockMovementType.Adjustment,
                    QuantityChange = difference,
                    UnitCostMinor = unitCost,
                    OccurredAt = now,
                    Note = note
                });
            }

            resultLines.Add(new StockCountLineResponse
            {
                ProductId = line.ProductId,
                Name = product.Name,
                ExpectedQuantity = expected,
                CountedQuantity = line.CountedQuantity,
                Difference = difference,
                UnitCostMinor = unitCost,
                ValueMinor = value
            });
        }

        var response = new StockCountResponse
        {
            CountDate = request.CountDate,
            LinesCounted = request.Lines.Count,
            LinesAdjusted = movements.Count,
            MissingValueMinor = missingValue,
            FoundValueMinor = foundValue,
            Lines = resultLines
        };

        // Every line matched: nothing to write, not even an empty entry.
        if (movements.Count == 0)
        {
            response.Message = "Every count matched what Salio shows. Nothing was written.";
            return response;
        }

        // Found minus missing. Above zero the shelf gained value, below zero
        // it lost value. Missing and found net into one entry.
        long net = foundValue - missingValue;

        JournalEntry? journalEntry = null;
        var journalLines = new List<JournalLine>();

        if (net != 0)
        {
            var accounts = await _db.Accounts
                .AsNoTracking()
                .Where(a => a.OrganizationId == access.OrganizationId && a.IsActive)
                .ToListAsync(cancellationToken);

            Guid inventoryAccountId = FindAccountId(accounts, InventoryCode);
            Guid stockLossAccountId = FindAccountId(accounts, StockLossCode);

            if (inventoryAccountId == Guid.Empty || stockLossAccountId == Guid.Empty)
            {
                return BadRequest(Rejected(
                    "This shop is missing one of the accounts a stock count needs: 1200 Inventory, 5700 Stock Loss. Nothing was saved."));
            }

            journalEntry = new JournalEntry
            {
                Id = Guid.NewGuid(),
                OrganizationId = access.OrganizationId,
                EntryDate = request.CountDate,
                Description = "Stock count",
                SourceType = SourceType.StockAdjustment,
                SourceId = countId,
                CreatedAt = now
            };

            if (net < 0)
            {
                // Stock missing: a cost to the shop, and the shelf is worth less.
                journalLines.Add(NewLine(access.OrganizationId, stockLossAccountId, -net, 0));
                journalLines.Add(NewLine(access.OrganizationId, inventoryAccountId, 0, -net));
            }
            else
            {
                // Stock found: the shelf is worth more, and the loss shrinks.
                journalLines.Add(NewLine(access.OrganizationId, inventoryAccountId, net, 0));
                journalLines.Add(NewLine(access.OrganizationId, stockLossAccountId, 0, net));
            }
        }

        // One transaction around both halves, as a stock receipt does it.
        // Nothing is final until CommitAsync; any failure rolls it all back.
        using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Journal rows go through JournalPoster, stock rows through
            // StockPoster, never through _db here.
            if (journalEntry is not null)
            {
                await _journalPoster.PostAsync(journalEntry, journalLines, cancellationToken);
            }

            await _stockPoster.PostAsync(movements, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // For example a period closed by a filed tax return.
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(Rejected(ex.Message));
        }

        response.Id = countId;
        response.JournalEntryId = journalEntry?.Id;

        if (journalEntry is null)
        {
            // The differences were real but their values cancelled out
            // exactly, or were all at a cost of zero, so the books do not move.
            response.Message = "The stock was corrected. The differences add up to nothing in value, "
                + "so no journal entry was needed.";
        }
        else if (net < 0)
        {
            response.Message = "The stock was corrected. The shop lost stock worth "
                + Shillings(-net) + ".";
        }
        else
        {
            response.Message = "The stock was corrected. The shop found stock worth "
                + Shillings(net) + ".";
        }

        return response;
    }

    // 123450 minor units is "KES 1,234.50". Integer arithmetic only.
    private static string Shillings(long minor)
    {
        return "KES " + (minor / 100).ToString("N0") + "." + (minor % 100).ToString("00");
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

    private static ProblemDetails Rejected(string detail) => new()
    {
        Title = "The stock count was rejected",
        Detail = detail,
        Status = StatusCodes.Status400BadRequest
    };
}
