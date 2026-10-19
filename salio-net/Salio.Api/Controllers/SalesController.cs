using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Salio.Api.Dtos;
using Salio.Api.Security;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;
using Salio.Infrastructure;

namespace Salio.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/sales")]
public class SalesController : ControllerBase
{
    private readonly SalioDbContext _db;
    private readonly SaleService _saleService;
    private readonly StockService _stockService;
    private readonly JournalPoster _journalPoster;
    private readonly StockPoster _stockPoster;
    private readonly CustomerLedger _customerLedger;
    private readonly OrganizationGuard _guard;

    public SalesController(
        SalioDbContext db,
        SaleService saleService,
        StockService stockService,
        JournalPoster journalPoster,
        StockPoster stockPoster,
        CustomerLedger customerLedger,
        OrganizationGuard guard)
    {
        _db = db;
        _saleService = saleService;
        _stockService = stockService;
        _journalPoster = journalPoster;
        _stockPoster = stockPoster;
        _customerLedger = customerLedger;
        _guard = guard;
    }

    [HttpPost]
    public async Task<ActionResult<SaleResponse>> Create(
        CreateSaleRequest request,
        CancellationToken cancellationToken)
    {
        // Rule 9: the shop is the one this user is a member of, never the id
        // as sent. Everything below uses access.OrganizationId.
        var access = await _guard.ForAsync(User, request.OrganizationId, cancellationToken);

        if (!access.IsAllowed)
        {
            return OrganizationGuard.Deny(access);
        }

        if (!OrganizationGuard.TryGetUserId(User, out Guid sellerId))
        {
            return Unauthorized();
        }

        var accounts = await _db.Accounts
            .AsNoTracking()
            .Where(a => a.OrganizationId == access.OrganizationId && a.IsActive)
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

        // A credit sale: the goods leave now and the customer owes the
        // total. Checked here, before anything is written. Mixing credit
        // with cash or M-Pesa is refused by SaleService.
        bool onCredit = false;

        foreach (var paymentRequest in request.Payments)
        {
            if (paymentRequest.Method == PaymentMethod.Credit)
            {
                onCredit = true;
            }
        }

        Guid receivableAccountId = FindAccountId(accounts, CustomerLedger.ReceivableCode);
        Customer? customer = null;

        if (onCredit)
        {
            if (receivableAccountId == Guid.Empty)
            {
                return BadRequest(Rejected(
                    "This organisation is missing account 1100 Accounts Receivable, which a credit sale needs"));
            }

            if (request.CustomerId is null)
            {
                return BadRequest(Rejected("A credit sale needs a customer"));
            }

            // Rule 9: the customer must be one of this shop's.
            customer = await _db.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    c => c.Id == request.CustomerId
                        && c.OrganizationId == access.OrganizationId
                        && c.IsActive,
                    cancellationToken);

            if (customer is null)
            {
                return BadRequest(Rejected("That customer is not in this shop"));
            }
        }

        var sale = new Sale
        {
            Id = Guid.NewGuid(),
            OrganizationId = access.OrganizationId,
            SaleNumber = await NextSaleNumberAsync(access.OrganizationId, cancellationToken),
            SaleDate = request.SaleDate,
            OccurredAt = DateTimeOffset.UtcNow,
            Status = SaleStatus.Completed,

            // Who served this customer. Rule 13 needs it so a cashier can be
            // shown their own sales.
            AppUserId = sellerId,

            CustomerId = customer?.Id
        };

        // Rule 9 again: a product id from the till is only trusted once it
        // is known to belong to this shop. Without this, one shop could sell
        // another shop's product, and an unknown id became a 500.
        foreach (var lineRequest in request.Lines)
        {
            bool productIsOurs = await _db.Products
                .AsNoTracking()
                .AnyAsync(
                    p => p.Id == lineRequest.ProductId
                        && p.OrganizationId == access.OrganizationId,
                    cancellationToken);

            if (!productIsOurs)
            {
                return BadRequest(Rejected(
                    "That product is not in this shop: " + lineRequest.ProductId));
            }
        }

        var saleLines = new List<SaleLine>();
        var movements = new List<StockMovement>();
        long totalMinor = 0;

        foreach (var lineRequest in request.Lines)
        {
            // What this product currently costs the shop, from its own
            // movement history. Never a figure the caller supplies.
            long unitCostMinor = await UnitCostAsync(
                access.OrganizationId, lineRequest.ProductId, cancellationToken);

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
                cashAccountId, mpesaAccountId, salesAccountId, cogsAccountId, inventoryAccountId,
                receivableAccountId, customer?.Id);

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

        // What a credit customer owes now, this sale included, for the
        // receipt. Read back from the journal, never added up here.
        long? customerBalanceMinor = null;

        if (customer is not null)
        {
            customerBalanceMinor = await _customerLedger.BalanceAsync(
                access.OrganizationId, customer.Id, cancellationToken);
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
            JournalEntryId = journalEntry.Id,
            CustomerId = customer?.Id,
            CustomerName = customer?.Name,
            CustomerBalanceMinor = customerBalanceMinor
        };
    }

    // Locked rule 13: a Cashier sees the sales they made; an Owner sees the
    // whole shop's. Sales from before logins existed have no seller, so only
    // the Owner sees those.
    [HttpGet]
    public async Task<ActionResult<List<SaleSummaryResponse>>> GetAll(
        [FromQuery] Guid? organizationId,
        [FromQuery][BindRequired] DateOnly from,
        [FromQuery][BindRequired] DateOnly to,
        CancellationToken cancellationToken)
    {
        var access = await _guard.ForAsync(User, organizationId, cancellationToken);

        if (!access.IsAllowed)
        {
            return OrganizationGuard.Deny(access);
        }

        if (!OrganizationGuard.TryGetUserId(User, out Guid userId))
        {
            return Unauthorized();
        }

        if (to < from)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Bad period",
                Detail = "The period ends before it starts",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var sales = _db.Sales
            .AsNoTracking()
            .Where(s => s.OrganizationId == access.OrganizationId
                && s.SaleDate >= from
                && s.SaleDate <= to);

        if (!access.IsOwner)
        {
            sales = sales.Where(s => s.AppUserId == userId);
        }

        var rows = await sales
            .OrderByDescending(s => s.OccurredAt)
            .ToListAsync(cancellationToken);

        var saleIds = new List<Guid>();

        foreach (var sale in rows)
        {
            saleIds.Add(sale.Id);
        }

        // Every payment for the whole list in ONE query, then matched up in
        // memory. Asking per sale would be the N+1 problem, the same trap
        // ProductsController avoids with stock movements.
        var payments = await _db.Payments
            .AsNoTracking()
            .Where(p => p.OrganizationId == access.OrganizationId && saleIds.Contains(p.SaleId))
            .ToListAsync(cancellationToken);

        // Seller names, in one query for the whole list rather than one per
        // sale. The ids come from this shop's own sales, so no other shop's
        // people can appear.
        var sellerIds = new List<Guid>();

        foreach (var sale in rows)
        {
            if (sale.AppUserId is Guid sellerId && !sellerIds.Contains(sellerId))
            {
                sellerIds.Add(sellerId);
            }
        }

        var sellers = await _db.AppUsers
            .AsNoTracking()
            .Where(u => sellerIds.Contains(u.Id))
            .ToListAsync(cancellationToken);

        var response = new List<SaleSummaryResponse>();

        foreach (var sale in rows)
        {
            string? soldBy = null;

            foreach (var seller in sellers)
            {
                if (seller.Id == sale.AppUserId)
                {
                    soldBy = seller.FullName;
                }
            }

            response.Add(new SaleSummaryResponse
            {
                Id = sale.Id,
                SaleNumber = sale.SaleNumber,
                SaleDate = sale.SaleDate,
                OccurredAt = sale.OccurredAt,
                TotalMinor = sale.TotalMinor,
                Status = sale.Status,
                SoldByUserId = sale.AppUserId,
                SoldBy = soldBy,
                Payments = PaymentsFor(sale.Id, payments)
            });
        }

        return response;
    }

    // One sale, as a receipt reads. Anything the caller may not see is a 404,
    // never a 403: a different answer for "exists but not yours" would let
    // someone learn which ids are real by guessing.
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SaleDetailResponse>> GetById(
        Guid id,
        [FromQuery] Guid? organizationId,
        CancellationToken cancellationToken)
    {
        var access = await _guard.ForAsync(User, organizationId, cancellationToken);

        if (!access.IsAllowed)
        {
            return OrganizationGuard.Deny(access);
        }

        if (!OrganizationGuard.TryGetUserId(User, out Guid userId))
        {
            return Unauthorized();
        }

        // Query 1 of 3: the sale, with the seller's name joined on. The cast
        // makes a left join legal, because AppUserId is nullable and Id is
        // not; a sale with no seller still comes back, with a null name.
        var found = await (
            from row in _db.Sales.AsNoTracking()
            join seller in _db.AppUsers.AsNoTracking()
                on row.AppUserId equals (Guid?)seller.Id into sellers
            from seller in sellers.DefaultIfEmpty()
            where row.Id == id && row.OrganizationId == access.OrganizationId
            select new
            {
                Sale = row,
                SoldBy = seller == null ? null : seller.FullName
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (found is null)
        {
            return NotFound();
        }

        var sale = found.Sale;

        // Who took it on credit, for the receipt. Null for any other sale.
        string? customerName = null;

        if (sale.CustomerId is Guid customerId)
        {
            customerName = await _db.Customers
                .AsNoTracking()
                .Where(c => c.Id == customerId && c.OrganizationId == access.OrganizationId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        // A Cashier may read their own sales only (locked rule 13). Someone
        // else's sale is treated exactly like one that does not exist.
        if (!access.IsOwner && sale.AppUserId != userId)
        {
            return NotFound();
        }

        // Query 2 of 3: this sale's payments.
        var payments = await _db.Payments
            .AsNoTracking()
            .Where(p => p.OrganizationId == access.OrganizationId && p.SaleId == sale.Id)
            .ToListAsync(cancellationToken);

        // Query 3 of 3: the lines, with each product's name and SKU joined
        // on, so no name is fetched one at a time. UnitCostMinor is on the
        // line and is deliberately not read: selling price only.
        var lines = await (
            from line in _db.SaleLines.AsNoTracking()
            join product in _db.Products.AsNoTracking()
                on line.ProductId equals product.Id
            where line.OrganizationId == access.OrganizationId
                && product.OrganizationId == access.OrganizationId
                && line.SaleId == sale.Id
            orderby product.Name
            select new SaleLineResponse
            {
                ProductName = product.Name,
                Sku = product.Sku,
                Quantity = line.Quantity,
                UnitPriceMinor = line.UnitPriceMinor
            })
            .ToListAsync(cancellationToken);

        return new SaleDetailResponse
        {
            Id = sale.Id,
            SaleNumber = sale.SaleNumber,
            SaleDate = sale.SaleDate,
            OccurredAt = sale.OccurredAt,
            TotalMinor = sale.TotalMinor,
            Payments = PaymentsFor(sale.Id, payments),
            SoldBy = found.SoldBy,
            CustomerName = customerName,
            Lines = lines
        };
    }

    // Picks one sale's payments out of a list already loaded.
    private static List<SalePaymentResponse> PaymentsFor(Guid saleId, List<Payment> payments)
    {
        var result = new List<SalePaymentResponse>();

        foreach (var payment in payments)
        {
            if (payment.SaleId == saleId)
            {
                result.Add(new SalePaymentResponse
                {
                    Method = payment.Method,
                    AmountMinor = payment.AmountMinor
                });
            }
        }

        return result;
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
