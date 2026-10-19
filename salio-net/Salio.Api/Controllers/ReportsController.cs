using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Salio.Api.Security;
using Salio.Infrastructure;
using Salio.Infrastructure.Reports;

namespace Salio.Api.Controllers;

/// <summary>
/// Read-only. Every figure is derived on read, so there is nothing here to
/// POST, refresh or recalculate.
/// </summary>
[ApiController]
[Authorize]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly ReportService _reports;
    private readonly OrganizationGuard _guard;

    public ReportsController(ReportService reports, OrganizationGuard guard)
    {
        _reports = reports;
        _guard = guard;
    }

    [HttpGet("trial-balance")]
    public async Task<ActionResult<TrialBalanceReport>> GetTrialBalance(
        [FromQuery] Guid? organizationId,
        [FromQuery][BindRequired] DateOnly asOf,
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

        return await _reports.GetTrialBalanceAsync(access.OrganizationId, asOf, cancellationToken);
    }

    [HttpGet("profit-and-loss")]
    public async Task<ActionResult<ProfitAndLossReport>> GetProfitAndLoss(
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

        if (!access.IsOwner)
        {
            return OrganizationGuard.OwnerOnly();
        }

        return await _reports.GetProfitAndLossAsync(access.OrganizationId, from, to, cancellationToken);
    }

    [HttpGet("stock-valuation")]
    public async Task<ActionResult<StockValuationReport>> GetStockValuation(
        [FromQuery] Guid? organizationId,
        [FromQuery][BindRequired] DateOnly asOf,
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

        return await _reports.GetStockValuationAsync(access.OrganizationId, asOf, cancellationToken);
    }

    [HttpGet("day-sheet")]
    public async Task<ActionResult<DaySheetReport>> GetDaySheet(
        [FromQuery] Guid? organizationId,
        [FromQuery][BindRequired] DateOnly date,
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

        return await _reports.GetDaySheetAsync(access.OrganizationId, date, cancellationToken);
    }
}
