using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Salio.Api.Dtos;
using Salio.Api.Security;
using Salio.Domain.Entities;
using Salio.Infrastructure;

namespace Salio.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/journal-entries")]
public class JournalController : ControllerBase
{
    private readonly JournalPoster _journalPoster;
    private readonly SalioDbContext _db;
    private readonly OrganizationGuard _guard;

    public JournalController(JournalPoster journalPoster, SalioDbContext db, OrganizationGuard guard)
    {
        _journalPoster = journalPoster;
        _db = db;
        _guard = guard;
    }

    [HttpPost]
    public async Task<ActionResult<JournalEntryResponse>> Create(
        CreateJournalEntryRequest request,
        CancellationToken cancellationToken)
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

        var entry = new JournalEntry
        {
            Id = Guid.NewGuid(),
            OrganizationId = access.OrganizationId,
            EntryDate = request.EntryDate,
            Description = request.Description,
            SourceType = request.SourceType,
            SourceId = request.SourceId,
            CreatedAt = DateTimeOffset.UtcNow
        };

        var lines = new List<JournalLine>();

        foreach (var lineRequest in request.Lines)
        {
            // JournalEntryId is left unset on purpose. PostJournal stamps it,
            // so there is one place that decides which entry a line belongs to.
            lines.Add(new JournalLine
            {
                Id = Guid.NewGuid(),
                OrganizationId = entry.OrganizationId,
                AccountId = lineRequest.AccountId,
                DebitMinor = lineRequest.DebitMinor,
                CreditMinor = lineRequest.CreditMinor,
                Currency = lineRequest.Currency
            });
        }

        try
        {
            await _journalPoster.PostAsync(entry, lines, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // This is the ledger refusing the entry: unbalanced, or a line with
            // both a debit and a credit. The caller needs to read the reason,
            // so the message is passed through untouched rather than replaced
            // with a generic one.
            return BadRequest(new ProblemDetails
            {
                Title = "The journal entry was rejected",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }

        var response = ToResponse(entry, lines);

        return CreatedAtAction(
            nameof(GetById),
            new { id = entry.Id, organizationId = entry.OrganizationId },
            response);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<JournalEntryResponse>> GetById(
        Guid id,
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

        // Filtering on OrganizationId as well as Id is not belt and braces: it
        // is what stops one shop reading another shop's books by guessing an id.
        var entry = await _db.JournalEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.Id == id && e.OrganizationId == access.OrganizationId,
                cancellationToken);

        if (entry is null)
        {
            return NotFound();
        }

        var lines = await _db.JournalLines
            .AsNoTracking()
            .Where(l => l.JournalEntryId == entry.Id && l.OrganizationId == access.OrganizationId)
            .OrderBy(l => l.AccountId)
            .ToListAsync(cancellationToken);

        return ToResponse(entry, lines);
    }

    private static JournalEntryResponse ToResponse(JournalEntry entry, List<JournalLine> lines)
    {
        var response = new JournalEntryResponse
        {
            Id = entry.Id,
            OrganizationId = entry.OrganizationId,
            EntryDate = entry.EntryDate,
            Description = entry.Description,
            SourceType = entry.SourceType,
            SourceId = entry.SourceId,
            CreatedAt = entry.CreatedAt
        };

        foreach (var line in lines)
        {
            response.Lines.Add(new JournalLineResponse
            {
                Id = line.Id,
                AccountId = line.AccountId,
                DebitMinor = line.DebitMinor,
                CreditMinor = line.CreditMinor,
                Currency = line.Currency
            });
        }

        return response;
    }
}
