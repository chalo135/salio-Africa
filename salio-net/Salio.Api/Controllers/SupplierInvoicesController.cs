using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Salio.Api.Security;
using Salio.Domain.Services;
using Salio.Infrastructure.Invoices;

namespace Salio.Api.Controllers;

// A supplier's invoice, photographed or as a PDF: read into proposed lines,
// checked by the owner, then received into stock as ONE stock receipt.
// Owner only: it shows costs and moves stock value (locked rule 13).
[ApiController]
[Authorize]
[Route("api/supplier-invoices")]
public class SupplierInvoicesController : ControllerBase
{
    // A little above the 10 MB file limit, for the multipart wrapping.
    private const long UploadLimitBytes = 11 * 1024 * 1024;

    private readonly SupplierInvoiceService _invoices;
    private readonly IInvoiceReader _reader;

    // The same object as _reader, asked a different question: is a real
    // reader switched on? (GET reader-status.)
    private readonly ConfiguredInvoiceReader _configuredReader;
    private readonly OrganizationGuard _guard;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;

    public SupplierInvoicesController(
        SupplierInvoiceService invoices,
        IInvoiceReader reader,
        ConfiguredInvoiceReader configuredReader,
        OrganizationGuard guard,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        _invoices = invoices;
        _reader = reader;
        _configuredReader = configuredReader;
        _guard = guard;
        _configuration = configuration;
        _environment = environment;
    }

    // Can this server really read an invoice? { "real": true } only when a
    // real, built reader is configured. False for the demo reader, and false
    // when a key is set for a reader nobody has built (uploads answer 503).
    // The screen asks this before it offers "photograph an invoice".
    [HttpGet("reader-status")]
    public async Task<ActionResult<InvoiceReaderStatus>> ReaderStatus(
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

        return new InvoiceReaderStatus { Real = _configuredReader.IsReal };
    }

    // Upload and read. Posts nothing to the books.
    //
    // The form is read by hand, not bound to a parameter, so that a file over
    // the limit gets one plain sentence instead of a validation dump.
    [HttpPost]
    [ReadFormByHand]
    [RequestSizeLimit(UploadLimitBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadLimitBytes)]
    public async Task<ActionResult<InvoiceView>> Upload(
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

        if (!OrganizationGuard.TryGetUserId(User, out Guid userId))
        {
            return Unauthorized();
        }

        if (!Request.HasFormContentType)
        {
            return BadRequest(Problem400("Send the invoice as a file in a form field named \"file\"."));
        }

        IFormCollection form;

        try
        {
            form = await Request.ReadFormAsync(cancellationToken);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new ProblemDetails
            {
                Title = "The invoice was not uploaded",
                Detail = InvoiceFile.TooBigMessage,
                Status = StatusCodes.Status413PayloadTooLarge
            });
        }
        catch (InvalidDataException)
        {
            // The multipart limit, or a broken form.
            return BadRequest(Problem400(InvoiceFile.TooBigMessage));
        }

        IFormFile? file = form.Files.GetFile("file");

        if (file is null || file.Length == 0)
        {
            return BadRequest(Problem400("Choose a photo or PDF of the invoice."));
        }

        if (file.Length > InvoiceFile.MaxBytes)
        {
            return BadRequest(Problem400(InvoiceFile.TooBigMessage));
        }

        byte[] content;

        using (var buffer = new MemoryStream())
        {
            await file.CopyToAsync(buffer, cancellationToken);
            content = buffer.ToArray();
        }

        var (contentType, extension) = InvoiceFile.Sniff(content);

        if (contentType is null || extension is null)
        {
            return BadRequest(Problem400(InvoiceFile.WrongTypeMessage));
        }

        string? folder = StorageFolder(out string? storageProblem);

        if (folder is null)
        {
            return Problem503(storageProblem ?? "Invoice storage is not set up.");
        }

        Directory.CreateDirectory(folder);

        Guid invoiceId = Guid.NewGuid();
        string fileName = invoiceId + extension;
        string fullPath = Path.Combine(folder, fileName);

        await System.IO.File.WriteAllBytesAsync(fullPath, content, cancellationToken);

        try
        {
            InvoiceExtraction extraction;

            using (var stream = new MemoryStream(content, writable: false))
            {
                extraction = await _reader.ReadAsync(stream, contentType, cancellationToken);
            }

            // The demo reader invents six motorcycle parts whatever the
            // photo shows. Fine for trying the screen in Development. In a
            // real shop those lines could be received into stock and posted
            // to the books, which can never be deleted, so outside
            // Development a demo reading is refused and nothing is kept.
            if (extraction.IsDemo && !_environment.IsDevelopment())
            {
                System.IO.File.Delete(fullPath);
                return Problem503(
                    "Reading invoices is not switched on yet. Add the stock by hand for now.");
            }

            return await _invoices.CreateAsync(
                access.OrganizationId, userId, invoiceId, fileName, extraction, cancellationToken);
        }
        catch (InvoiceReaderNotBuiltException ex)
        {
            // Nothing is kept when the invoice could not be read.
            System.IO.File.Delete(fullPath);
            return Problem503(ex.Message);
        }
        catch
        {
            System.IO.File.Delete(fullPath);
            throw;
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InvoiceView>> Get(
        Guid id, [FromQuery] Guid? organizationId, CancellationToken cancellationToken)
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

        var view = await _invoices.GetAsync(access.OrganizationId, id, cancellationToken);

        if (view is null)
        {
            return NotFoundProblem();
        }

        return view;
    }

    // The owner's corrections. Status becomes Reviewed.
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<InvoiceView>> Update(
        Guid id,
        UpdateSupplierInvoiceRequest request,
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

        var outcome = await _invoices.UpdateAsync(access.OrganizationId, id, request, cancellationToken);

        return Answer(outcome);
    }

    // ONE stock receipt for the ticked lines, through StockReceiptWriter.
    [HttpPost("{id:guid}/commit")]
    public async Task<ActionResult<CommitSupplierInvoiceResult>> Commit(
        Guid id,
        CommitSupplierInvoiceRequest request,
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

        // Who received it: new parts made here go into the price history.
        Guid? userId = OrganizationGuard.TryGetUserId(User, out Guid signedIn) ? signedIn : null;

        var outcome = await _invoices.CommitAsync(access.OrganizationId, id, request, userId, cancellationToken);

        return Answer(outcome);
    }

    // Thrown away. The file and the rows are kept; nothing was posted.
    [HttpPost("{id:guid}/discard")]
    public async Task<IActionResult> Discard(
        Guid id, [FromQuery] Guid? organizationId, CancellationToken cancellationToken)
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

        var outcome = await _invoices.DiscardAsync(access.OrganizationId, id, cancellationToken);

        if (outcome.Value is not null)
        {
            return NoContent();
        }

        return Answer(outcome).Result!;
    }

    // ---- Helpers ----

    // D10: "InvoiceStorage:Path", relative to the content root unless it is
    // a full path. In Development an empty setting means App_Data/invoices.
    // Never inside wwwroot, where files would be served to anyone.
    private string? StorageFolder(out string? problem)
    {
        problem = null;
        string configured = (_configuration["InvoiceStorage:Path"] ?? "").Trim();

        if (configured.Length == 0)
        {
            if (!_environment.IsDevelopment())
            {
                problem = "InvoiceStorage:Path is not set, so invoices cannot be stored.";
                return null;
            }

            configured = Path.Combine("App_Data", "invoices");
        }

        string folder = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, configured));

        string? webRoot = _environment.WebRootPath;

        if (!string.IsNullOrEmpty(webRoot)
            && folder.StartsWith(Path.GetFullPath(webRoot), StringComparison.OrdinalIgnoreCase))
        {
            problem = "InvoiceStorage:Path points inside wwwroot. Choose a folder that is not served to the web.";
            return null;
        }

        return folder;
    }

    private ActionResult<T> Answer<T>(InvoiceOutcome<T> outcome) where T : class
    {
        if (outcome.NotFound)
        {
            return NotFoundProblem();
        }

        // A closed period: 409, the month in words, and today's date so the
        // screen can offer "post it today" by sending receiptDate = today.
        if (outcome.ClosedPeriod)
        {
            var closed = new ProblemDetails
            {
                Title = "The receipt was not posted",
                Detail = outcome.Error,
                Status = StatusCodes.Status409Conflict
            };

            closed.Extensions["closedPeriod"] = true;
            closed.Extensions["today"] = StockMovementDates.KenyaDateOf(DateTimeOffset.UtcNow)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            return Conflict(closed);
        }

        if (outcome.Conflict is not null)
        {
            return Conflict(new ProblemDetails
            {
                Title = "The invoice was not changed",
                Detail = outcome.Conflict,
                Status = StatusCodes.Status409Conflict
            });
        }

        if (outcome.Error is not null)
        {
            return BadRequest(Problem400(outcome.Error));
        }

        return outcome.Value!;
    }

    private NotFoundObjectResult NotFoundProblem() => NotFound(new ProblemDetails
    {
        Title = "Not found",
        Detail = "That invoice is not in this shop.",
        Status = StatusCodes.Status404NotFound
    });

    private ObjectResult Problem503(string detail) => StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
    {
        Title = "The invoice could not be read",
        Detail = detail,
        Status = StatusCodes.Status503ServiceUnavailable
    });

    private static ProblemDetails Problem400(string detail) => new()
    {
        Title = "The invoice was not accepted",
        Detail = detail,
        Status = StatusCodes.Status400BadRequest
    };
}
