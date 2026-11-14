using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Salio.Domain.Entities;
using Salio.Infrastructure;

namespace Salio.Api.Security;

/// <summary>
/// The lock on the admin area (/api/admin/*). Runs before every admin
/// request, ahead of model binding, and lets it through only for a person
/// whose AppUser row says they are the platform admin.
///
/// The flag is read fresh from the database on every request, never from
/// the cookie, so taking it away in psql shuts the door on the next click.
///
/// Everybody else (a stranger, a signed-in shop owner, a switched-off admin)
/// gets a bare 404: the same status, headers and empty body as a path that
/// does not exist. Never 401 or 403, which would admit the area is there.
///
/// This class only READS the flag. Nothing anywhere may set it; see
/// PlatformAdminTests.No_request_can_change_IsPlatformAdmin.
/// </summary>
public class PlatformAdminFilter : IAsyncAuthorizationFilter
{
    private readonly SalioDbContext _db;

    public PlatformAdminFilter(SalioDbContext db)
    {
        _db = db;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // No readable user in the cookie (or no cookie): not found.
        if (!OrganizationGuard.TryGetUserId(context.HttpContext.User, out Guid userId))
        {
            context.Result = new BareNotFoundResult();
            return;
        }

        var user = await _db.AppUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, context.HttpContext.RequestAborted);

        if (!Allows(user))
        {
            context.Result = new BareNotFoundResult();
        }
    }

    // The whole rule, kept apart so a test can check it without a database.
    // A person still on a temporary password is refused too: until they
    // choose their own, they may do nothing else (PasswordChangeGate).
    public static bool Allows(AppUser? user)
    {
        if (user is null)
        {
            return false;
        }

        return user.IsActive
            && !user.MustChangePassword
            && user.IsPlatformAdmin;
    }
}

/// <summary>
/// A 404 with no body and no content type: exactly what ASP.NET Core sends
/// for a path no endpoint matches. NotFound() is not used, because under
/// [ApiController] it adds a ProblemDetails body an unknown path never has.
/// </summary>
public class BareNotFoundResult : IActionResult
{
    public Task ExecuteResultAsync(ActionContext context)
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }
}
