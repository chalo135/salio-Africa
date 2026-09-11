using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Salio.Api.Dtos;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;
using Salio.Infrastructure;

namespace Salio.Api.Controllers;

[ApiController]
[Route("api/sales")]
public class SalesController : ControllerBase
{
    private readonly SalioDbContext _db;
    private readonly SaleService _saleService;
    private readonly StockService _stockService;
    private readonly JournalPoster _journalPoster;
    private readonly StockPoster _stockPoster;

    public SalesController(
        SalioDbContext db,
        SaleService saleService,
        StockService stockService,
        JournalPoster journalPoster,
        StockPoster stockPoster)
    {
        _db = db;
        _saleService = saleService;
        _stockService = stockService;
        _journalPoster = journalPoster;
        _stockPoster = stockPoster;
    }

    [HttpPost]
    public async Task<ActionResult<SaleResponse>> Create(
        CreateSaleRequest request,
        CancellationToken cancellationToken)
    {
        var accounts = await _db.Accounts
            .AsNoTracking()
            .Where(a => a.OrganizationId == request.OrganizationId && a.IsActive)
            .ToListAsync(cancellationToken);

        Guid cashAccountId = FindAccountId(accounts, "1000");
        Guid mpesaAccountId = FindAccountId(accounts, "1010");
        Guid inventoryAccountId = FindAccountId(accounts, "1200");
        Guid salesAccountId = FindAccountId(accounts, "4000");
        Guid cogsAccountId = FindAccountId(accounts, "5000");

        if (cashAccountId == Guid.Empty || mpesaAccountId == Guid.Empty
            || inventoryAccountId == Guid.Empty
            || salesAccountId == Guid.Empty || cogsAccountId == Guid.Empty)
        {
            return BadRequest(Rejected(
                "This organisation is missing one of the accounts a sale needs: 1000, 1010, 1200, 4000, 5000"));
        }

        var sale = new Sale
        {
            Id = Guid.NewGuid(),
            OrganizationId = request.OrganizationId,
            SaleNumber = await NextSaleNumberAsync(request.OrganizationId, cancellationToken),
            SaleDate = request.SaleDate,
            OccurredAt = DateTimeOffset.UtcNow,
            Status = SaleStatus.Completed
        };

        var saleLines = new List<SaleLine>();
        var movements = new List<StockMovement>();
        long totalMinor = 0;

        foreach (var lineRequest in request.Lines)
        {
            // What this product currently costs the shop, from its own
            // movement history. Never a figure the caller supplies.
            long unitCostMinor = await UnitCostAsync(
                request.OrganizationId, lineRequest.ProductId, cancellationToken);

            long lineTotalMinor = (long)Math.Round(
                lineRequest.Quantity * lineRequest.UnitPriceMinor,
                MidpointRounding.AwayFromZero);

            saleLines.Add(new SaleLine
            {
                Id = Guid.NewGuid(),
                OrganizationId = sale.OrganizationId,
                SaleId = sale.Id,
                ProductId = lineRequest.ProductId,
                Quantity = lineRequest.Quantity,
                UnitPriceMinor = lineRequest.UnitPriceMinor,
                LineTotalMinor = lineTotalMinor,
                UnitCostMinor = unitCostMinor,
                TaxBand = lineRequest.TaxBand
            });

            // Stock leaving the shelf is a negative movement, never an update
            // to an existing row.
            movements.Add(new StockMovement
            {
                Id = Guid.NewGuid(),
                OrganizationId = sale.OrganizationId,
                ProductId = lineRequest.ProductId,
                Type = StockMovementType.Sale,
                QuantityChange = -lineRequest.Quantity,
                UnitCostMinor = unitCostMinor,
                OccurredAt = sale.OccurredAt,
                Note = sale.SaleNumber
            });

            totalMinor = totalMinor + lineTotalMinor;
        }

        sale.TotalMinor = totalMinor;

        var payments = new List<Payment>();

        foreach (var paymentRequest in request.Payments)
        {
            payments.Add(new Payment
            {
                Id = Guid.NewGuid(),
                OrganizationId = sale.OrganizationId,
                SaleId = sale.Id,
                Method = paymentRequest.Method,
                AmountMinor = paymentRequest.AmountMinor,
                Reference = paymentRequest.Reference,
                ReceivedAt = sale.OccurredAt
            });
        }

        var journalEntry = new JournalEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = sale.OrganizationId,
            EntryDate = sale.SaleDate,
            Description = "Sale " + sale.SaleNumber,
            SourceType = SourceType.Sale,
            SourceId = sale.Id,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // One transaction around all of it. The receipt, the journal entry and
        // the stock movements land together or not at all: a sale that books
        // revenue without moving stock would be worse than no sale.
        using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var journalLines = _saleService.BuildJournalLines(
                sale, saleLines, payments,
                cashAccountId, mpesaAccountId, salesAccountId, cogsAccountId, inventoryAccountId);

            _db.Sales.Add(sale);
            _db.SaleLines.AddRange(saleLines);
            _db.Payments.AddRange(payments);
            await _db.SaveChangesAsync(cancellationToken);

            // Journal rows go through JournalPoster, never through _db here.
            await _journalPoster.PostAsync(journalEntry, journalLines, cancellationToken);
            await _stockPoster.PostAsync(movements, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(Rejected(ex.Message));
        }

        return new SaleResponse
        {
            Id = sale.Id,
            OrganizationId = sale.OrganizationId,
            SaleNumber = sale.SaleNumber,
            SaleDate = sale.SaleDate,
            OccurredAt = sale.OccurredAt,
            TotalMinor = sale.TotalMinor,
            Status = sale.Status,
            JournalEntryId = journalEntry.Id
        };
    }

    private async Task<long> UnitCostAsync(
        Guid organizationId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        var movements = await _db.StockMovements
            .AsNoTracking()
            .Where(m => m.OrganizationId == organizationId && m.ProductId == productId)
            .ToListAsync(cancellationToken);

        return _stockService.CalculateWeightedAverageCost(movements);
    }

    private async Task<string> NextSaleNumberAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        int soFar = await _db.Sales
            .CountAsync(s => s.OrganizationId == organizationId, cancellationToken);

        return "S-" + (soFar + 1).ToString("D6");
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
        Title = "The sale was rejected",
        Detail = detail,
        Status = StatusCodes.Status400BadRequest
    };
}
