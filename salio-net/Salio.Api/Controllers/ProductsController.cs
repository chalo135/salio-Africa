using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Salio.Api.Dtos;
using Salio.Api.Security;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;
using Salio.Infrastructure;
using Salio.Infrastructure.Security;

namespace Salio.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly SalioDbContext _db;
    private readonly StockService _stockService;
    private readonly OpeningStockWriter _openingStock;
    private readonly OrganizationGuard _guard;
    private readonly ImportedCostFinder _importedCosts;

    public ProductsController(
        SalioDbContext db,
        StockService stockService,
        OpeningStockWriter openingStock,
        OrganizationGuard guard,
        ImportedCostFinder importedCosts)
    {
        _db = db;
        _stockService = stockService;
        _openingStock = openingStock;
        _guard = guard;
        _importedCosts = importedCosts;
    }

    // The shelf list. Quantity is never stored: it is the sum of every
    // movement ever written for the product, added up here on each request.
    //
    // includeInactive is for the Owner's Parts screen, which lists retired
    // parts too. A Cashier asking for it still gets the shelf only.
    [HttpGet]
    public async Task<ActionResult<List<ProductStockResponse>>> GetAll(
        [FromQuery] Guid? organizationId,
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken)
    {
        // Rule 9: the shop comes from the membership, not from the query.
        var access = await _guard.ForAsync(User, organizationId, cancellationToken);

        if (!access.IsAllowed)
        {
            return OrganizationGuard.Deny(access);
        }

        bool showRetired = includeInactive && access.IsOwner;

        // A retired part (IsActive false) is left out here, and this list is
        // all the till has: its search filters this list in the browser. So
        // this one condition is what keeps a retired part off the shelf and
        // out of search.
        var products = await _db.Products.AsNoTracking()
            .Where(p => p.OrganizationId == access.OrganizationId && (p.IsActive || showRetired))
            .OrderBy(p => p.Sku)
            .ToListAsync(cancellationToken);

        // Everything received counts positive, everything sold negative, so
        // one sum per product is the quantity on hand.
        var movements = await _db.StockMovements.AsNoTracking()
            .Where(m => m.OrganizationId == access.OrganizationId)
            .ToListAsync(cancellationToken);

        // One cost decision for the whole list (CostVisibilityRules), and one
        // look at which costs an import brought in (CostProvenanceRules).
        PricingView pricing = await PricingViewAsync(access, cancellationToken);
        var importedMovementIds = await _importedCosts.LiveImportedMovementIdsAsync(
            access.OrganizationId, cancellationToken);

        var response = new List<ProductStockResponse>();

        foreach (var product in products)
        {
            var productMovements = new List<StockMovement>();

            foreach (var movement in movements)
            {
                if (movement.ProductId == product.Id)
                {
                    productMovements.Add(movement);
                }
            }

            response.Add(BuildResponse(product, productMovements, _stockService, pricing, importedMovementIds));
        }

        return response;
    }

    // What the caller may see of the shop's buying price, and the shop's
    // margin for stop prices. Worked out once per request.
    public record PricingView(bool CanSeeCost, int MinMarginPercent);

    private async Task<PricingView> PricingViewAsync(
        MembershipAccessResult access, CancellationToken cancellationToken)
    {
        var organization = await _db.Organizations.AsNoTracking()
            .FirstAsync(o => o.Id == access.OrganizationId, cancellationToken);

        // The guard only grants access through an active membership, so a
        // caller who got this far is an active member. Whether they are the
        // platform admin is asked of PlatformAdminFilter.Allows, the one
        // place that reads that column (PlatformAdminTests), so this file
        // never names it.
        AppUser? caller = null;

        if (OrganizationGuard.TryGetUserId(User, out Guid userId))
        {
            caller = await _db.AppUsers.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        }

        var context = new CostVisibilityContext(
            access.Role,
            organization.CashierSeesCost,
            IsActiveMember: access.IsAllowed,
            PlatformAdmin: PlatformAdminFilter.Allows(caller));

        return new PricingView(
            CostVisibilityRules.CanSeeCost(context),
            organization.MinMarginPercent);
    }

    // One line of the shelf list, from a product and its own movements.
    // Static so a test can check exactly what goes to the browser.
    [NonAction]
    public static ProductStockResponse BuildResponse(
        Product product, List<StockMovement> productMovements,
        StockService stockService, PricingView pricing,
        IReadOnlySet<Guid> importedMovementIds)
    {
        // The counting and costing rules live in StockService and stay
        // there. A product that has never moved gets 0 from both.
        decimal quantityOnHand = stockService.CurrentQuantity(productMovements);
        bool needsReceiving = quantityOnHand < 0;

        // Below zero, weighted average cost means nothing, so no figure
        // is used at all rather than one that looks right (rule 16).
        long? averageCostMinor = null;

        if (!needsReceiving)
        {
            averageCostMinor = stockService.CalculateWeightedAverageCost(productMovements);
        }

        bool costIsConfirmed = CostProvenanceRules.CostIsConfirmed(productMovements, importedMovementIds);

        // Guidance (the stop, the margin note) rests on what a person last
        // paid, not the weighted average: that survives selling out, and an
        // imported cost never counts. None stated gives no stop, the same
        // path as no cost at all. Nothing here is ever posted.
        long? costForPricing = CostProvenanceRules.LastBoughtAtMinor(productMovements, importedMovementIds);

        return new ProductStockResponse
        {
            Id = product.Id,
            Sku = product.Sku,
            Name = product.Name,
            SellPriceMinor = product.SellPriceMinor,
            BinLocation = product.BinLocation,
            Category = product.Category,
            Barcode = product.Barcode,
            ReorderLevel = product.ReorderLevel,
            IsActive = product.IsActive,
            QuantityOnHand = quantityOnHand,
            NeedsReceiving = needsReceiving,

            // The stop is sent whatever the caller may see. The cost is not:
            // when the gate says no it is left out of the JSON entirely.
            StopPriceMinor = PricingRules.StopPrice(
                product.MinPriceMinor, costForPricing, pricing.MinMarginPercent),
            BuyingPriceShown = pricing.CanSeeCost,
            AverageCostMinor = pricing.CanSeeCost ? averageCostMinor : null,

            // Sent with the cost, never without it.
            CostIsConfirmed = pricing.CanSeeCost ? costIsConfirmed : null,
            LastBoughtAtMinor = pricing.CanSeeCost ? costForPricing : null,

            // Stated costs only: an imported cost is not the owner's.
            MarginUnderRulePercent =
                pricing.CanSeeCost
                && PricingRules.MarginUnderRule(product.SellPriceMinor, costForPricing, pricing.MinMarginPercent)
                    ? pricing.MinMarginPercent
                    : null
        };
    }

    // The same shape for one product, after it has been changed.
    private async Task<ProductStockResponse> ToResponseAsync(
        Product product, MembershipAccessResult access, CancellationToken cancellationToken)
    {
        var productMovements = await _db.StockMovements.AsNoTracking()
            .Where(m => m.OrganizationId == product.OrganizationId && m.ProductId == product.Id)
            .ToListAsync(cancellationToken);

        PricingView pricing = await PricingViewAsync(access, cancellationToken);
        var importedMovementIds = await _importedCosts.LiveImportedMovementIdsAsync(
            access.OrganizationId, cancellationToken);

        return BuildResponse(product, productMovements, _stockService, pricing, importedMovementIds);
    }

    // Inactive products are still found here: old sales point at them.
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Product>> GetById(
        Guid id, [FromQuery] Guid? organizationId, CancellationToken cancellationToken)
    {
        var access = await _guard.ForAsync(User, organizationId, cancellationToken);

        if (!access.IsAllowed)
        {
            return OrganizationGuard.Deny(access);
        }

        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.Id == id && p.OrganizationId == access.OrganizationId, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }
        return product;
    }

    // Adding to the catalogue is not selling, so it is the Owner's job
    // (locked rule 13).
    [HttpPost]
    public async Task<ActionResult<Product>> Create(CreateProductRequest request, CancellationToken cancellationToken)
    {
        var access = await _guard.ForAsync(User, request.OrganizationId, cancellationToken);

        if (!access.IsAllowed)
        {
            return OrganizationGuard.Deny(access);
        }

        if (!access.IsOwner)
        {
            return OrganizationGuard.OwnerOnly();
        }

        // The unique index would reject a duplicate with a 500. Say what is wrong instead.
        bool skuTaken = await _db.Products.AnyAsync(
            p => p.OrganizationId == access.OrganizationId && p.Sku == request.Sku, cancellationToken);

        if (skuTaken)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Not added",
                Detail = "SKU " + request.Sku + " is already used in this shop",
                Status = StatusCodes.Status409Conflict
            });
        }

        var product = new Product
        {
            Id = Guid.NewGuid(),
            OrganizationId = access.OrganizationId,
            Sku = request.Sku,
            Name = request.Name,
            SellPriceMinor = request.SellPriceMinor,
            BinLocation = request.BinLocation,
            IsActive = true
        };
        _db.Products.Add(product);
        PriceChanges.Record(_db, product, null, CurrentUserId());
        await _db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = product.Id, organizationId = product.OrganizationId }, product);
    }

    // Change a part's name, SKU, selling price or shelf. Owner only.
    // Old receipts are not affected: each sale line keeps the name and SKU
    // it was sold under, and its own price.
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProductStockResponse>> Update(
        Guid id,
        [FromQuery] Guid? organizationId,
        UpdateProductRequest request,
        CancellationToken cancellationToken)
    {
        var access = await _guard.ForAsync(User, organizationId, cancellationToken);

        if (!access.IsAllowed)
        {
            return OrganizationGuard.Deny(access);
        }

        if (!access.IsOwner)
        {
            return OrganizationGuard.OwnerOnly();
        }

        // Anything sent beyond the four fields is refused, by name, so nobody
        // believes they changed a quantity or a cost when nothing happened.
        if (request.Unexpected is not null && request.Unexpected.Count > 0)
        {
            string firstField = request.Unexpected.Keys.First();
            return BadRequest(Rejected(WhyNotChangeable(firstField)));
        }

        var product = await _db.Products.FirstOrDefaultAsync(
            p => p.Id == id && p.OrganizationId == access.OrganizationId, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        string sku = request.Sku.Trim();

        // The unique index would reject a duplicate with a 500. Say which part has it.
        var skuOwner = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(
                p => p.OrganizationId == access.OrganizationId && p.Sku == sku && p.Id != id,
                cancellationToken);

        if (skuOwner is not null)
        {
            return Conflict(new ProblemDetails
            {
                Title = "The part was not changed",
                Detail = "SKU " + sku + " is already used by " + skuOwner.Name,
                Status = StatusCodes.Status409Conflict
            });
        }

        string? binLocation = request.BinLocation?.Trim();

        long oldPrice = product.SellPriceMinor;

        product.Name = request.Name.Trim();
        product.Sku = sku;
        product.SellPriceMinor = request.SellPriceMinor ?? product.SellPriceMinor;
        product.BinLocation = string.IsNullOrEmpty(binLocation) ? null : binLocation;

        PriceChanges.Record(_db, product, oldPrice, CurrentUserId());

        await _db.SaveChangesAsync(cancellationToken);

        return await ToResponseAsync(product, access, cancellationToken);
    }

    // Retire a part. Never a delete: its sales and stock movements are what
    // the reports are made of. It leaves the shelf list and the till's
    // search; old sales and the stock valuation still show it.
    [HttpPost("{id:guid}/deactivate")]
    public Task<ActionResult<ProductStockResponse>> Deactivate(
        Guid id, [FromQuery] Guid? organizationId, CancellationToken cancellationToken)
    {
        return SetActiveAsync(id, organizationId, false, cancellationToken);
    }

    // Put a retired part back on the shelf.
    [HttpPost("{id:guid}/activate")]
    public Task<ActionResult<ProductStockResponse>> Activate(
        Guid id, [FromQuery] Guid? organizationId, CancellationToken cancellationToken)
    {
        return SetActiveAsync(id, organizationId, true, cancellationToken);
    }

    private async Task<ActionResult<ProductStockResponse>> SetActiveAsync(
        Guid id, Guid? organizationId, bool isActive, CancellationToken cancellationToken)
    {
        var access = await _guard.ForAsync(User, organizationId, cancellationToken);

        if (!access.IsAllowed)
        {
            return OrganizationGuard.Deny(access);
        }

        if (!access.IsOwner)
        {
            return OrganizationGuard.OwnerOnly();
        }

        var product = await _db.Products.FirstOrDefaultAsync(
            p => p.Id == id && p.OrganizationId == access.OrganizationId, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        // Asking twice is harmless: the answer is the same either way.
        product.IsActive = isActive;
        await _db.SaveChangesAsync(cancellationToken);

        return await ToResponseAsync(product, access, cancellationToken);
    }

    // What the quick-add form may ask for in this shop. Stock (quantity and
    // cost) only until the shop's first sale; after that, name and price.
    // Owner and Cashier, like quick-add itself.
    [HttpGet("quick-add/rules")]
    public async Task<ActionResult<QuickAddRulesResponse>> QuickAddRules(
        [FromQuery] Guid? organizationId, CancellationToken cancellationToken)
    {
        var access = await _guard.ForAsync(User, organizationId, cancellationToken);

        if (!access.IsAllowed)
        {
            return OrganizationGuard.Deny(access);
        }

        bool hasSold = await _openingStock.ShopHasSoldAsync(access.OrganizationId, cancellationToken);

        return new QuickAddRulesResponse { StockAllowed = !hasSold };
    }

    // A part added at the counter when a search finds nothing (decision D8).
    // Owner and Cashier: a till never refuses a sale, so whoever is serving
    // can add the part and sell it. The server makes the SKU. Cost and
    // quantity are Owner matters (locked rule 13): from a Cashier they are
    // ignored and nothing is posted.
    [HttpPost("quick-add")]
    public async Task<ActionResult<ProductStockResponse>> QuickAdd(
        [FromQuery] Guid? organizationId,
        QuickAddProductRequest request,
        CancellationToken cancellationToken)
    {
        var access = await _guard.ForAsync(User, organizationId, cancellationToken);

        if (!access.IsAllowed)
        {
            return OrganizationGuard.Deny(access);
        }

        string name = (request.Name ?? "").Trim();

        if (name.Length == 0)
        {
            return BadRequest(NotAdded("Give the part a name."));
        }

        if (name.Length > 200)
        {
            return BadRequest(NotAdded("Keep the name to 200 characters or fewer."));
        }

        if (request.SellPriceMinor is null)
        {
            return BadRequest(NotAdded("Enter the selling price."));
        }

        if (request.SellPriceMinor.Value < 0)
        {
            return BadRequest(NotAdded("The selling price cannot be below zero."));
        }

        decimal quantity = 0;
        long unitCost = 0;

        if (access.IsOwner)
        {
            quantity = request.Quantity ?? 0;
            unitCost = request.UnitCostMinor ?? 0;

            // Quick-add stock is opening stock (Owner's Equity). Once the shop
            // has made a sale, new stock was bought, so it belongs in Stock in
            // (cash or a payable), never here. Name and price still save and
            // still sell. OpeningStockWriter would refuse anyway; this says
            // it in quick-add's own words, and also catches a cost sent alone.
            if ((quantity != 0 || unitCost != 0)
                && await _openingStock.ShopHasSoldAsync(access.OrganizationId, cancellationToken))
            {
                return BadRequest(NotAdded(
                    "This shop has already made a sale, so stock cannot be added here. "
                    + "Save the part with its name and price only, then use Stock in to record the stock you bought."));
            }

            if (unitCost < 0)
            {
                return BadRequest(NotAdded("A unit cost cannot be negative."));
            }

            if (quantity < 0)
            {
                return BadRequest(NotAdded("The quantity cannot be below zero."));
            }

            // Stock at no cost would make every sale of it look like pure
            // profit, so a quantity needs its cost. The part itself does not.
            if (quantity > 0 && unitCost == 0)
            {
                return BadRequest(NotAdded("Add a unit cost to record the quantity, or leave the quantity empty."));
            }

            // A cost lives only on stock coming in. With no quantity there is
            // nothing to carry it, and dropping it silently would mislead.
            if (unitCost > 0 && quantity == 0)
            {
                return BadRequest(NotAdded("Add the quantity on the shelf to record the unit cost, or leave the unit cost empty."));
            }
        }

        // Shared with "add as a new part" on a supplier invoice.
        string? sku = await ProductSkus.GenerateFreeAsync(_db, access.OrganizationId, cancellationToken);

        if (sku is null)
        {
            return BadRequest(NotAdded("Salio could not make a free SKU for the part. Try again."));
        }

        var product = new Product
        {
            Id = Guid.NewGuid(),
            OrganizationId = access.OrganizationId,
            Sku = sku,
            Name = name,
            SellPriceMinor = request.SellPriceMinor.Value,
            IsActive = true
        };

        if (quantity == 0)
        {
            _db.Products.Add(product);
            PriceChanges.Record(_db, product, null, CurrentUserId());
            await _db.SaveChangesAsync(cancellationToken);

            return await ToResponseAsync(product, access, cancellationToken);
        }

        // The part and its opening stock land together, or neither does.
        using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        _db.Products.Add(product);
        PriceChanges.Record(_db, product, null, CurrentUserId());
        await _db.SaveChangesAsync(cancellationToken);

        var lines = new List<StockLineInput>
        {
            new() { ProductId = product.Id, Quantity = quantity, UnitCostMinor = unitCost }
        };

        // Opening stock, under OpeningStockWriter's own rule: refused once
        // the shop has made any sale.
        var result = await _openingStock.WriteAsync(
            access.OrganizationId,
            JournalReverser.TodayInNairobi(),
            lines,
            sourceId: null,
            cancellationToken);

        if (result.Error is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            return BadRequest(NotAdded(result.Error));
        }

        await transaction.CommitAsync(cancellationToken);

        return await ToResponseAsync(product, access, cancellationToken);
    }

    // Parts needing a cost (decision D9). Owner only: it is about cost.
    [HttpGet("needing-cost")]
    public async Task<ActionResult<List<NeedingCostResponse>>> NeedingCost(
        [FromQuery] Guid? organizationId, CancellationToken cancellationToken)
    {
        var access = await _guard.ForAsync(User, organizationId, cancellationToken);

        if (!access.IsAllowed)
        {
            return OrganizationGuard.Deny(access);
        }

        if (!access.IsOwner)
        {
            return OrganizationGuard.OwnerOnly();
        }

        return await FindNeedingCostAsync(access.OrganizationId, cancellationToken);
    }

    // For the badge on the menu. A Cashier gets 0: the list is not theirs.
    [HttpGet("needing-cost/count")]
    public async Task<ActionResult<NeedingCostCountResponse>> NeedingCostCount(
        [FromQuery] Guid? organizationId, CancellationToken cancellationToken)
    {
        var access = await _guard.ForAsync(User, organizationId, cancellationToken);

        if (!access.IsAllowed)
        {
            return OrganizationGuard.Deny(access);
        }

        if (!access.IsOwner)
        {
            return new NeedingCostCountResponse { Count = 0 };
        }

        var list = await FindNeedingCostAsync(access.OrganizationId, cancellationToken);
        return new NeedingCostCountResponse { Count = list.Count };
    }

    // Active parts whose weighted average cost (StockService) is zero AND
    // which have sold or have stock. Read only: past sales keep the cost
    // they were booked at; a cost entered now applies from now on.
    private async Task<List<NeedingCostResponse>> FindNeedingCostAsync(
        Guid organizationId, CancellationToken cancellationToken)
    {
        var products = await _db.Products.AsNoTracking()
            .Where(p => p.OrganizationId == organizationId && p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        var movements = await _db.StockMovements.AsNoTracking()
            .Where(m => m.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);

        // Finished sales only: a draft never reached the books.
        var saleLines = await _db.SaleLines.AsNoTracking()
            .Where(l => l.OrganizationId == organizationId)
            .Join(_db.Sales.Where(s => s.Status != SaleStatus.Draft),
                l => l.SaleId, s => s.Id,
                (l, s) => new { l.ProductId, l.Quantity, l.LineTotalMinor, l.UnitCostMinor })
            .ToListAsync(cancellationToken);

        var response = new List<NeedingCostResponse>();

        foreach (var product in products)
        {
            var productMovements = movements.Where(m => m.ProductId == product.Id).ToList();
            long averageCost = _stockService.CalculateWeightedAverageCost(productMovements);
            decimal onHand = _stockService.CurrentQuantity(productMovements);

            if (averageCost != 0)
            {
                continue;
            }

            var lines = saleLines.Where(l => l.ProductId == product.Id).ToList();

            if (lines.Count == 0 && onHand == 0)
            {
                continue;
            }

            long withoutCost = lines.Where(l => l.UnitCostMinor == 0).Sum(l => l.LineTotalMinor);

            // Sold out at a real cost: the average reads zero only because
            // the shelf is empty, and every sale carried its cost. Nothing to fix.
            if (onHand == 0 && withoutCost == 0)
            {
                continue;
            }

            response.Add(new NeedingCostResponse
            {
                ProductId = product.Id,
                Name = product.Name,
                Sku = product.Sku,
                QuantitySold = lines.Sum(l => l.Quantity),
                RevenueWithoutCostMinor = withoutCost,
                QuantityOnHand = onHand
            });
        }

        return response;
    }

    // Who is making a change, for the price history. Behind [Authorize] the
    // cookie always carries one; null only if it somehow does not.
    private Guid? CurrentUserId()
    {
        return OrganizationGuard.TryGetUserId(User, out Guid userId) ? userId : null;
    }

    private static ProblemDetails NotAdded(string detail) => new()
    {
        Title = "The part was not added",
        Detail = detail,
        Status = StatusCodes.Status400BadRequest
    };

    // A sentence for each field that cannot be changed through PUT.
    private static string WhyNotChangeable(string fieldName)
    {
        string name = fieldName.ToLowerInvariant();

        if (name.Contains("quantity"))
        {
            return "Quantity is never typed. It comes from stock movements: record a delivery through Stock in.";
        }

        if (name.Contains("cost"))
        {
            return "Cost is never typed. It comes from what deliveries cost, recorded through Stock in.";
        }

        if (name == "organizationid")
        {
            return "A part cannot move to another shop. Name the shop with ?organizationId= instead.";
        }

        if (name == "isactive")
        {
            return "To retire a part or bring it back, use deactivate or activate, not this.";
        }

        return fieldName + " cannot be changed here. Only name, SKU, selling price and shelf can.";
    }

    private static ProblemDetails Rejected(string detail) => new()
    {
        Title = "The part was not changed",
        Detail = detail,
        Status = StatusCodes.Status400BadRequest
    };
}
