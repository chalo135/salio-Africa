using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Salio.Api.Dtos;
using Salio.Api.Security;
using Salio.Infrastructure;
using Salio.Infrastructure.Tax;

namespace Salio.Api.Controllers;

/// <summary>
/// Salio works out the figure; the shopkeeper files it on iTax herself.
/// Nothing here talks to KRA.
/// </summary>
[ApiController]
[Authorize]
[Route("api/tax")]
public class TaxController : ControllerBase
{
    private readonly TaxService _tax;
    private readonly OrganizationGuard _guard;

    public TaxController(TaxService tax, OrganizationGuard guard)
    {
        _tax = tax;
        _guard = guard;
    }

    [HttpGet("turnover-tax")]
    public async Task<ActionResult<TurnoverTaxResult>> GetTurnoverTax(
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

        // Tax is the Owner's business (locked rule 13).
        if (!access.IsOwner)
        {
            return OrganizationGuard.OwnerOnly();
        }

        try
        {
            return await _tax.GetTurnoverTaxAsync(access.OrganizationId, from, to, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(Rejected(ex.Message));
        }
    }

    [HttpGet("filing-pack")]
    public async Task<ActionResult<FilingPack>> GetFilingPack(
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

        // Tax is the Owner's business (locked rule 13).
        if (!access.IsOwner)
        {
            return OrganizationGuard.OwnerOnly();
        }

        try
        {
            return await _tax.GetFilingPackAsync(access.OrganizationId, from, to, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(Rejected(ex.Message));
        }
    }

    [HttpPost("filed-returns")]
    public async Task<ActionResult<FiledReturnResponse>> RecordFiledReturn(
        RecordFiledReturnRequest request,
        CancellationToken cancellationToken)
    {
        var access = await _guard.ForAsync(User, request.OrganizationId, cancellationToken);

        if (!access.IsAllowed)
        {
            return OrganizationGuard.Deny(access);
        }

        // Tax is the Owner's business (locked rule 13).
        if (!access.IsOwner)
        {
            return OrganizationGuard.OwnerOnly();
        }

        try
        {
            var filed = await _tax.RecordFiledReturnAsync(
                access.OrganizationId,
                request.PeriodFrom,
                request.PeriodTo,
                request.TaxType,
                request.AmountMinor,
                request.FiledAt,
                request.AcknowledgementReference,
                cancellationToken);

            return new FiledReturnResponse
            {
                Id = filed.Id,
                OrganizationId = filed.OrganizationId,
                PeriodFrom = filed.PeriodFrom,
                PeriodTo = filed.PeriodTo,
                TaxType = filed.TaxType,
                AmountMinor = filed.AmountMinor,
                FiledAt = filed.FiledAt,
                AcknowledgementReference = filed.AcknowledgementReference
            };
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(Rejected(ex.Message));
        }
    }

    private static ProblemDetails Rejected(string detail) => new()
    {
        Title = "The tax request was rejected",
        Detail = detail,
        Status = StatusCodes.Status400BadRequest
    };
}
