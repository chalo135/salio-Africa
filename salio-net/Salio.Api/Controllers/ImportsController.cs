using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Salio.Api.Security;
using Salio.Domain.Services;
using Salio.Infrastructure;
using Salio.Infrastructure.Imports;

namespace Salio.Api.Controllers;

// The product import (CSV or Excel). Owner only: it creates parts, posts
// opening stock and so touches stock value (locked rule 13). Nothing is
// written until the owner has seen a preview and pressed Import (rule 15),
// and the commit checks the file again from scratch.
[ApiController]
[Authorize]
[Route("api/imports")]
public class ImportsController : ControllerBase
{
    // A little above the 5 MB file limit, for the multipart wrapping. A
    // file between the two gets the plain "larger than 5 MB" sentence.
    private const long UploadLimitBytes = 6 * 1024 * 1024;

    private readonly ProductImportService _imports;
    private readonly OrganizationGuard _guard;

    public ImportsController(ProductImportService imports, OrganizationGuard guard)
    {
        _imports = imports;
        _guard = guard;
    }

    // The template: the shop's own parts in exactly the import format.
    [HttpGet("products.csv")]
    public async Task<IActionResult> Export(
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

        var (content, fileName) = await _imports.ExportAsync(access.OrganizationId, cancellationToken);

        return File(content, "text/csv; charset=utf-8", fileName);
    }

    // Reads and checks the file. Writes nothing.
    [HttpPost("preview")]
    [RequestSizeLimit(UploadLimitBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimitBytes)]
    public async Task<ActionResult<ImportPreviewResult>> Preview(
        IFormFile? file, [FromQuery] Guid? organizationId, CancellationToken cancellationToken)
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

        if (file is null || file.Length == 0)
        {
            return BadRequest(Rejected("Choose a .csv or .xlsx file to import."));
        }

        if (file.Length > ImportFile.MaxBytes)
        {
            return new ImportPreviewResult
            {
                FileName = file.FileName,
                Errors = { new ImportIssue { Message = ImportFile.TooBigMessage } }
            };
        }

        byte[] content = await ReadAllAsync(file, cancellationToken);

        return await _imports.PreviewAsync(access.OrganizationId, file.FileName, content, cancellationToken);
    }

    // The same file again, checked again, then written in one transaction.
    //
    // Two more form fields, needed only when a new part carries a quantity:
    //   stockBasis: "OpeningStock" (stock already owned when the books
    //               opened) or "Purchase" (stock the shop bought). Any capitals.
    //   paidWith:   for a Purchase, the PaymentMethod number the Stock in
    //               screen sends: 0 Cash, 1 Mpesa, 3 Credit.
    // Both are read as text and checked by ImportStockBasisRules, so a wrong
    // value gets a plain sentence rather than a binding error.
    [HttpPost("commit")]
    [RequestSizeLimit(UploadLimitBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimitBytes)]
    public async Task<ActionResult<ImportCommitResult>> Commit(
        IFormFile? file,
        [FromForm] string? stockBasis,
        [FromForm] string? paidWith,
        [FromQuery] Guid? organizationId,
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

        if (!OrganizationGuard.TryGetUserId(User, out Guid userId))
        {
            return Unauthorized();
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest(Rejected("Choose a .csv or .xlsx file to import."));
        }

        if (file.Length > ImportFile.MaxBytes)
        {
            return BadRequest(Rejected(ImportFile.TooBigMessage));
        }

        byte[] content = await ReadAllAsync(file, cancellationToken);

        var outcome = await _imports.CommitAsync(
            access.OrganizationId, userId, file.FileName, content, stockBasis, paidWith, cancellationToken);

        if (outcome.Result is null)
        {
            var problem = Rejected(outcome.Detail ?? "Nothing was imported.");
            problem.Extensions["errors"] = outcome.Errors;
            return BadRequest(problem);
        }

        return outcome.Result;
    }

    // The import history, newest first.
    [HttpGet]
    public async Task<ActionResult<List<ImportBatchSummary>>> List(
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

        return await _imports.ListAsync(access.OrganizationId, cancellationToken);
    }

    [HttpPost("{batchId:guid}/undo")]
    public async Task<ActionResult<ImportUndoResult>> Undo(
        Guid batchId, [FromQuery] Guid? organizationId, CancellationToken cancellationToken)
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

        if (!OrganizationGuard.TryGetUserId(User, out Guid userId))
        {
            return Unauthorized();
        }

        var outcome = await _imports.UndoAsync(access.OrganizationId, userId, batchId, cancellationToken);

        if (outcome.NotFound)
        {
            return NotFound(new ProblemDetails
            {
                Title = "The import was not undone",
                Detail = "That import is not in this shop.",
                Status = StatusCodes.Status404NotFound
            });
        }

        if (outcome.AlreadyUndone)
        {
            return Conflict(new ProblemDetails
            {
                Title = "The import was not undone",
                Detail = "This import has already been undone.",
                Status = StatusCodes.Status409Conflict
            });
        }

        if (outcome.Result is null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "The import was not undone",
                Detail = outcome.Detail ?? "Nothing was undone.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        return outcome.Result;
    }

    private static async Task<byte[]> ReadAllAsync(IFormFile file, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private static ProblemDetails Rejected(string detail) => new()
    {
        Title = "The import was rejected",
        Detail = detail,
        Status = StatusCodes.Status400BadRequest
    };
}
