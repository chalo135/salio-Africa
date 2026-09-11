using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Salio.Infrastructure;
using Salio.Infrastructure.Reports;

namespace Salio.Api.Controllers;

/// <summary>
/// Read-only. Every figure is derived on read, so there is nothing here to
/// POST, refresh or recalculate.
/// </summary>
[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly ReportService _reports;

    public ReportsController(ReportService reports)
    {
        _reports = reports;
    }

    [HttpGet("trial-balance")]
    public async Task<ActionResult<TrialBalanceReport>> GetTrialBalance(
        [FromQuery][BindRequired] Guid organizationId,
        [FromQuery][BindRequired] DateOnly asOf,
        CancellationToken cancellationToken)
    {
        return await _reports.GetTrialBalanceAsync(organizationId, asOf, cancellationToken);
    }

    [HttpGet("profit-and-loss")]
    public async Task<ActionResult<ProfitAndLossReport>> GetProfitAndLoss(
        [FromQuery][BindRequired] Guid organizationId,
        [FromQuery][BindRequired] DateOnly from,
        [FromQuery][BindRequired] DateOnly to,
        CancellationToken cancellationToken)
    {
        return await _reports.GetProfitAndLossAsync(organizationId, from, to, cancellationToken);
    }

    [HttpGet("stock-valuation")]
    public async Task<ActionResult<StockValuationReport>> GetStockValuation(
        [FromQuery][BindRequired] Guid organizationId,
        [FromQuery][BindRequired] DateOnly asOf,
        CancellationToken cancellationToken)
    {
        return await _reports.GetStockValuationAsync(organizationId, asOf, cancellationToken);
    }

    [HttpGet("day-sheet")]
    public async Task<ActionResult<DaySheetReport>> GetDaySheet(
        [FromQuery][BindRequired] Guid organizationId,
        [FromQuery][BindRequired] DateOnly date,
        CancellationToken cancellationToken)
    {
        return await _reports.GetDaySheetAsync(organizationId, date, cancellationToken);
    }
}
