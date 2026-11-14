using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Salio.Api.Dtos;
using Salio.Api.Security;
using Salio.Infrastructure;

namespace Salio.Api.Controllers;

// What a shop already owns on its shelves on the day it starts using Salio.
// Stock goes up, and the books say the owner brought it in: debit Inventory,
// credit Owner's Equity. No cash moves and nothing is owed, because that
// money left the shop long ago. Owner only, because it is stock value
// (locked rule 13).
[ApiController]
[Authorize]
[Route("api/opening-stock")]
public class OpeningStockController : ControllerBase
{
    private readonly OpeningStockWriter _writer;
    private readonly OrganizationGuard _guard;

    public OpeningStockController(OpeningStockWriter writer, OrganizationGuard guard)
    {
        _writer = writer;
        _guard = guard;
    }

    [HttpPost]
    public async Task<ActionResult<OpeningStockResponse>> Create(
        CreateOpeningStockRequest request,
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

        var lines = new List<StockLineInput>();

        foreach (var line in request.Lines)
        {
            lines.Add(new StockLineInput
            {
                ProductId = line.ProductId,
                Quantity = line.Quantity,
                UnitCostMinor = line.UnitCostMinor
            });
        }

        // Every check, the accounts (1200, 3000) and the posting live in
        // OpeningStockWriter, shared with the import and quick-add.
        var result = await _writer.WriteAsync(
            access.OrganizationId,
            request.OpeningDate,
            lines,
            sourceId: null,
            cancellationToken);

        if (result.Error is not null)
        {
            return BadRequest(Rejected(result.Error));
        }

        return new OpeningStockResponse
        {
            Id = result.OpeningId,
            OpeningDate = request.OpeningDate,
            TotalCostMinor = result.TotalCostMinor,
            LineCount = result.LineCount,
            JournalEntryId = result.JournalEntryId
        };
    }

    private static ProblemDetails Rejected(string detail) => new()
    {
        Title = "The opening stock was rejected",
        Detail = detail,
        Status = StatusCodes.Status400BadRequest
    };
}
