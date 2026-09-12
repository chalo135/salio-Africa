using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Salio.Api.Dtos;
using Salio.Domain.Entities;
using Salio.Infrastructure;

namespace Salio.Api.Controllers;

[ApiController]
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly SalioDbContext _db;

    public ProductsController(SalioDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<Product>>> GetAll(
        [FromQuery][BindRequired] Guid organizationId, CancellationToken cancellationToken)
    {
        return await _db.Products.AsNoTracking()
            .Where(p => p.OrganizationId == organizationId && p.IsActive)
            .OrderBy(p => p.Sku)
            .ToListAsync(cancellationToken);
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
            IsActive = true
        };
        _db.Products.Add(product);
        await _db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = product.Id, organizationId = product.OrganizationId }, product);
    }
}
