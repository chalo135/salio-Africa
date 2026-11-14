using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Salio.Api.Dtos;
using Salio.Api.Security;
using Salio.Infrastructure;

namespace Salio.Api.Controllers;

// Goods arriving from a supplier: stock goes up, and the money goes out or
// is owed. Owner only, because it is money and stock value (locked rule 13).
[ApiController]
[Authorize]
[Route("api/stock-receipts")]
public class StockReceiptsController : ControllerBase
{
    private readonly StockReceiptWriter _writer;
    private readonly OrganizationGuard _guard;

    public StockReceiptsController(StockReceiptWriter writer, OrganizationGuard guard)
    {
        _writer = writer;
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

        // Every check, the accounts (1000, 1010, 1200, 2000) and the posting
        // live in StockReceiptWriter, shared with the invoice reader.
        var result = await _writer.WriteAsync(
            access.OrganizationId,
            request.ReceiptDate,
            request.PaidWith,
            request.SupplierName,
            lines,
            cancellationToken);

        if (result.Error is not null)
        {
            return BadRequest(Rejected(result.Error));
        }

        return new StockReceiptResponse
        {
            Id = result.ReceiptId,
            ReceiptDate = request.ReceiptDate,
            TotalCostMinor = result.TotalCostMinor,
            LineCount = result.LineCount,
            JournalEntryId = result.JournalEntryId
        };
    }

    private static ProblemDetails Rejected(string detail) => new()
    {
        Title = "The stock receipt was rejected",
        Detail = detail,
        Status = StatusCodes.Status400BadRequest
    };
}
