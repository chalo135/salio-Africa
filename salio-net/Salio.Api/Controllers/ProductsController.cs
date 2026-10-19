using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Salio.Api.Dtos;
using Salio.Api.Security;
using Salio.Domain.Entities;
using Salio.Domain.Services;
using Salio.Infrastructure;

namespace Salio.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly SalioDbContext _db;
    private readonly StockService _stockService;
    private readonly OrganizationGuard _guard;

    public ProductsController(
        SalioDbContext db,
        StockService stockService,
        OrganizationGuard guard)
    {
        _db = db;
        _stockService = stockService;
        _guard = guard;
    }

    // The shelf list. Quantity is never stored: it is the sum of every
    // movement ever written for the product, added up here on each request.
    [HttpGet]
    public async Task<ActionResult<List<ProductStockResponse>>> GetAll(
        [FromQuery] Guid? organizationId, CancellationToken cancellationToken)
    {
        // Rule 9: the shop comes from the membership, not from the query.
        var access = await _guard.ForAsync(User, organizationId, cancellationToken);

        if (!access.IsAllowed)
        {
            return OrganizationGuard.Deny(access);
        }

        var products = await _db.Products.AsNoTracking()
            .Where(p => p.OrganizationId == access.OrganizationId && p.IsActive)
            .OrderBy(p => p.Sku)
            .ToListAsync(cancellationToken);

        // Everything received counts positive, everything sold negative, so
        // one sum per product is the quantity on hand.
        var movements = await _db.StockMovements.AsNoTracking()
            .Where(m => m.OrganizationId == access.OrganizationId)
            .ToListAsync(cancellationToken);

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

            // The counting and costing rules live in StockService and stay
            // there. A product that has never moved gets 0 from both.
            response.Add(new ProductStockResponse
            {
                Id = product.Id,
                Sku = product.Sku,
                Name = product.Name,
                SellPriceMinor = product.SellPriceMinor,
                BinLocation = product.BinLocation,
                QuantityOnHand = _stockService.CurrentQuantity(productMovements),
                AverageCostMinor = access.IsOwner
                    ? _stockService.CalculateWeightedAverageCost(productMovements)
                    : null
            });
        }

        return response;
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
                Title = "The product was rejected",
                Detail = "SKU " + request.Sku + " is already used by another product",
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
        await _db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = product.Id, organizationId = product.OrganizationId }, product);
    }
}
