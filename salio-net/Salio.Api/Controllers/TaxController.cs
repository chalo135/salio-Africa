using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Salio.Api.Dtos;
using Salio.Infrastructure;
using Salio.Infrastructure.Tax;

namespace Salio.Api.Controllers;

/// <summary>
/// Salio works out the figure; the shopkeeper files it on iTax herself.
/// Nothing here talks to KRA.
/// </summary>
[ApiController]
[Route("api/tax")]
public class TaxController : ControllerBase
{
    private readonly TaxService _tax;

    public TaxController(TaxService tax)
    {
        _tax = tax;
    }

    [HttpGet("turnover-tax")]
    public async Task<ActionResult<TurnoverTaxResult>> GetTurnoverTax(
        [FromQuery][BindRequired] Guid organizationId,
        [FromQuery][BindRequired] DateOnly from,
        [FromQuery][BindRequired] DateOnly to,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _tax.GetTurnoverTaxAsync(organizationId, from, to, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(Rejected(ex.Message));
        }
    }

    [HttpGet("filing-pack")]
    public async Task<ActionResult<FilingPack>> GetFilingPack(
        [FromQuery][BindRequired] Guid organizationId,
        [FromQuery][BindRequired] DateOnly from,
        [FromQuery][BindRequired] DateOnly to,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _tax.GetFilingPackAsync(organizationId, from, to, cancellationToken);
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
        try
        {
            var filed = await _tax.RecordFiledReturnAsync(
                request.OrganizationId,
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
