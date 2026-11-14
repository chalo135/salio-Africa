using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Salio.Infrastructure;

namespace Salio.Api.Controllers;

/// <summary>
/// "Is Salio up?" for the deploy scripts and the smoke test. Anyone may ask:
/// no login, no rate limit. It says only "ok" or "unavailable", never why:
/// the reason (which may hold a host name or a user name) stays out of the
/// answer.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/health")]
public class HealthController : ControllerBase
{
    // Long enough for a busy database, short enough that a poll never hangs.
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly SalioDbContext _db;
    private readonly ILogger<HealthController> _logger;

    public HealthController(SalioDbContext db, ILogger<HealthController> logger)
    {
        _db = db;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        bool databaseAnswers = false;

        // The first use after a start builds EF Core's picture of the tables,
        // which is slow and has nothing to do with the database. Done here,
        // before the clock starts, so the first poll is not a false alarm.
        _ = _db.Model;

        // Stops waiting after Patience, or as soon as the caller hangs up.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Patience);

        try
        {
            databaseAnswers = await _db.Database.CanConnectAsync(timeout.Token);
        }
        catch (Exception ex)
        {
            // Too slow, or refused. Either way: not healthy. Only the kind
            // of error is logged; its text can hold the connection details.
            _logger.LogWarning("Health check: the database did not answer ({Reason}).", ex.GetType().Name);
        }

        if (!databaseAnswers)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "unavailable" });
        }

        return Ok(new { status = "ok" });
    }
}
