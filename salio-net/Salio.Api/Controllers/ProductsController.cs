using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Salio.Api.Dtos;
using Salio.Domain.Entities;
using Salio.Domain.Services;
using Salio.Infrastructure;

namespace Salio.Api.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly SalioDbContext _db;
    private readonly StockService _stockService;

    public ProductsController(SalioDbContext db, StockService stockService)
    {
        _db = db;
        _stockService = stockService;
    }

    // The shelf list. Quantity is never stored: it is the sum of every
    // movement ever written for the product, added up here on each request.
    [HttpGet]
    public async Task<ActionResult<List<ProductStockResponse>>> GetAll(
        [FromQuery][BindRequired] Guid organizationId, CancellationToken cancellationToken)
    {
        var products = await _db.Products.AsNoTracking()
            .Where(p => p.OrganizationId == organizationId && p.IsActive)
            .OrderBy(p => p.Sku)
            .ToListAsync(cancellationToken);

        // Every movement in the shop, in ONE query. Asking the database per
        // product would be the N+1 problem: 13 products, 14 round trips.
        // Weighted average cost needs each movement in order, not just a sum,
        // so the rows are grouped here rather than summed by the database.
        var movements = await _db.StockMovements.AsNoTracking()
            .Where(m => m.OrganizationId == organizationId)
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
                AverageCostMinor = _stockService.CalculateWeightedAverageCost(productMovements)
            });
        }

        return response;
    }

    // Inactive products are still found here: old sales point at them.
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Product>> GetById(
        Guid id, [FromQuery][BindRequired] Guid organizationId, CancellationToken cancellationToken)
    {
        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.OrganizationId == organizationId, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }
        return product;
    }

    [HttpPost]
    public async Task<ActionResult<Product>> Create(CreateProductRequest request, CancellationToken cancellationToken)
    {
        // The unique index would reject a duplicate with a 500. Say what is wrong instead.
        bool skuTaken = await _db.Products.AnyAsync(
            p => p.OrganizationId == request.OrganizationId && p.Sku == request.Sku, cancellationToken);

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
            OrganizationId = request.OrganizationId,
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
