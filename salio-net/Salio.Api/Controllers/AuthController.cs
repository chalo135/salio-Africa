using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Salio.Api.Dtos;
using Salio.Domain.Entities;
using Salio.Infrastructure;
using Salio.Infrastructure.Security;

namespace Salio.Api.Controllers;

/// <summary>
/// Signing in and out. The cookie says who the person is and nothing more:
/// which shop a request may touch is decided per request from their
/// memberships (locked rule 9), never carried in the cookie.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    // One message for every kind of failed login. Telling the caller that the
    // email exists but the password is wrong would confirm who has an account.
    private const string LoginFailed = "Email or password is wrong.";

    // Also read in Program.cs, where every request's cookie is validated.
    public const string SecurityStampClaim = "salio:stamp";

    private readonly SalioDbContext _db;
    private readonly PasswordService _passwords;

    public AuthController(SalioDbContext db, PasswordService passwords)
    {
        _db = db;
        _passwords = passwords;
    }

    // Five attempts a minute per address (policy in Program.cs), then 429.
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    public async Task<ActionResult<SignedInUserResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        string emailLower = request.Email.ToLower();

        var user = await _db.AppUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email.ToLower() == emailLower, cancellationToken);

        // Unknown email, a person who has left, and a wrong password all end
        // the same way, with the same words.
        if (user is null || !user.IsActive || !_passwords.Check(user, request.Password))
        {
            return Unauthorized(Rejected(LoginFailed, StatusCodes.Status401Unauthorized));
        }

        var shops = await ShopsForAsync(user.Id, cancellationToken);

        // Signed in but attached to nothing: there is nothing they could do,
        // and this is a setup mistake worth naming plainly.
        if (shops.Count == 0)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                Rejected("This account is not attached to any shop.", StatusCodes.Status403Forbidden));
        }

        await SignInAsync(user);

        return SignedIn(user.Id, user.FullName, shops);
    }

    // The till calls this on start-up to find out whether it is still signed
    // in. [Authorize] here turns a missing or expired cookie into the 401 set
    // up in Program.cs. No existing endpoint has this attribute.
    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<SignedInUserResponse>> Me(CancellationToken cancellationToken)
    {
        string? idText = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(idText, out Guid userId))
        {
            return Unauthorized(Rejected(LoginFailed, StatusCodes.Status401Unauthorized));
        }

        var user = await _db.AppUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        // The cookie outlived the account, or the person has left since they
        // signed in. Throw the cookie away rather than honour it.
        if (user is null || !user.IsActive)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Unauthorized(Rejected(LoginFailed, StatusCodes.Status401Unauthorized));
        }

        var shops = await ShopsForAsync(user.Id, cancellationToken);

        return SignedIn(user.Id, user.FullName, shops);
    }

    // Always 200: signing out when you were never signed in is not an error,
    // and the till should never have to handle a failure here.
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok();
    }

    // The cookie carries the person, not the shop.
    private async Task SignInAsync(AppUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),

            // Checked against the database on every request, so an owner can
            // sign out every device without touching passwords (rule 12).
            new(SecurityStampClaim, user.SecurityStamp.ToString())
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                // The cookie survives closing the browser, so a cashier who
                // shuts the tab mid-shift is not asked to sign in again. The
                // 12 hour limit still comes from ExpireTimeSpan in
                // Program.cs, and sliding is off, so it ends 12 hours after
                // this moment whatever happens in between.
                IsPersistent = true
            });
    }

    private async Task<List<ShopMembershipResponse>> ShopsForAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await (
            from membership in _db.Memberships.AsNoTracking()
            join organization in _db.Organizations.AsNoTracking()
                on membership.OrganizationId equals organization.Id
            where membership.AppUserId == userId
            orderby organization.Name
            select new ShopMembershipResponse
            {
                OrganizationId = organization.Id,
                Name = organization.Name,
                Role = membership.Role
            })
            .ToListAsync(cancellationToken);
    }

    private static SignedInUserResponse SignedIn(
        Guid id,
        string fullName,
        List<ShopMembershipResponse> shops)
    {
        return new SignedInUserResponse
        {
            Id = id,
            FullName = fullName,
            Shops = shops
        };
    }

    private static ProblemDetails Rejected(string detail, int status) => new()
    {
        Title = "Sign in failed",
        Detail = detail,
        Status = status
    };
}
