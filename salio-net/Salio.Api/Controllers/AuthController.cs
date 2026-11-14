using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Salio.Api.Dtos;
using Salio.Api.Security;
using Salio.Domain.Entities;
using Salio.Domain.Services;
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

    // Signing in, the session cookie and the salio_device cookie (the proof
    // that this phone signed in with a password, so a PIN may unlock it).
    private readonly SignInService _signIn;

    // Wrong passwords per account: ten in ten minutes and the account waits,
    // whatever address the tries come from.
    private readonly SignInAttempts _attempts;

    public AuthController(
        SalioDbContext db,
        PasswordService passwords,
        SignInService signIn,
        SignInAttempts attempts)
    {
        _db = db;
        _passwords = passwords;
        _signIn = signIn;
        _attempts = attempts;
    }

    // Two limits. Per address: 10 a minute and 100 an hour (the "login"
    // policy in Program.cs), then 429. Per account: 10 wrong passwords in
    // 10 minutes (SignInAttempts), then 429 until the 10 minutes end.
    [EnableRateLimiting("login")]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        string emailLower = request.Email.ToLower();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Asked before the password is looked at, and for any email, real
        // or not, so the answer does not reveal which accounts exist.
        TimeSpan? wait = _attempts.WaitFor(emailLower, now);

        if (wait is not null)
        {
            return TooManyWrongPasswords(wait.Value);
        }

        var user = await _db.AppUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email.ToLower() == emailLower, cancellationToken);

        // Unknown email, a person who has left, and a wrong password all end
        // the same way, with the same words.
        if (user is null || !user.IsActive || !_passwords.Check(user, request.Password))
        {
            _attempts.RecordFailure(emailLower, now);
            return Unauthorized(Rejected(LoginFailed, StatusCodes.Status401Unauthorized));
        }

        _attempts.RecordSuccess(emailLower);

        var shops = await _signIn.ShopsForAsync(user.Id, cancellationToken);

        // Signed in but attached to nothing: there is nothing they could do,
        // and this is a setup mistake worth naming plainly.
        if (shops.Count == 0)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                Rejected("This account is not attached to any shop.", StatusCodes.Status403Forbidden));
        }

        // Signs in and trusts this phone, keeping its PIN if it already has
        // one for this person. The same code runs when setup finishes
        // (SignInService, shared with SetupController).
        bool pinSet = await _signIn.SignInAndTrustDeviceAsync(HttpContext, user, cancellationToken);

        return SignInService.LoggedIn(user, shops, pinSet);
    }

    // Whose phone is this? Asked when the session has gone, so no login is
    // required. 204 means "not a trusted phone": sign in with a password.
    [HttpGet("device")]
    public async Task<ActionResult<DeviceResponse>> Device(CancellationToken cancellationToken)
    {
        var found = await _signIn.ReadDeviceCookieAsync(HttpContext, cancellationToken);

        // Stale (the password changed, or every device was signed out): the
        // PIN cannot be used, so 204, but the cookie is KEPT. Deleting it
        // would make this phone look brand new, and the next password login
        // would ask for a new PIN instead of reviving this one.
        if (found is not null && found.IsStale)
        {
            return NoContent();
        }

        if (found is null || !found.IsTrusted)
        {
            _signIn.DeleteDeviceCookie(HttpContext);
            return NoContent();
        }

        return new DeviceResponse
        {
            FullName = found.User.FullName,
            PinSet = found.Device.PinHash is not null
        };
    }

    // Choosing a PIN for this phone, just after a password login.
    [Authorize]
    [HttpPost("pin")]
    public async Task<IActionResult> ChoosePin(PinRequest request, CancellationToken cancellationToken)
    {
        var found = await _signIn.ReadDeviceCookieAsync(HttpContext, cancellationToken);

        // The phone must be trusted, and trusted for the person signed in:
        // nobody sets a PIN on someone else's device.
        if (found is null
            || !found.IsTrusted
            || !OrganizationGuard.TryGetUserId(User, out Guid userId)
            || found.User.Id != userId)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                Rejected("This phone is not recognised. Sign in with your email and password again.",
                    StatusCodes.Status403Forbidden));
        }

        if (!PinRules.IsAcceptable(request.Pin))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "PIN not accepted",
                Detail = "Choose 4 digits that aren't all the same or in a row",
                Status = StatusCodes.Status400BadRequest
            });
        }

        found.Device.PinHash = _passwords.Hash(found.User, request.Pin);
        found.Device.FailedPinAttempts = 0;
        await _db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    // Unlocking a trusted phone with its PIN. Five wrong PINs in a row
    // revoke the device (PinUnlockRules), so a PIN cannot be guessed. Its
    // own rate limit, counted per phone ("pin-unlock" in Program.cs), not
    // the password one: a shop unlocks its tills all day, and must never
    // use up the allowance for signing in.
    [EnableRateLimiting("pin-unlock")]
    [HttpPost("pin-unlock")]
    public async Task<ActionResult<LoginResponse>> PinUnlock(
        PinRequest request,
        CancellationToken cancellationToken)
    {
        var found = await _signIn.ReadDeviceCookieAsync(HttpContext, cancellationToken);

        if (found is null || found.Device.PinHash is null)
        {
            return SignInWithPassword();
        }

        var device = found.Device;
        var user = found.User;

        bool pinMatches = _passwords.Check(user, device.PinHash, request.Pin);

        var result = PinUnlockRules.Decide(
            deviceRevoked: device.RevokedAt is not null,
            userIsActive: user.IsActive,
            deviceStamp: device.SecurityStamp,
            userStamp: user.SecurityStamp.ToString(),
            failedAttemptsSoFar: device.FailedPinAttempts,
            pinMatches: pinMatches);

        if (result.Outcome == PinUnlockOutcome.Unlocked)
        {
            var shops = await _signIn.ShopsForAsync(user.Id, cancellationToken);

            // Same refusal as a password login for a person with no shop.
            if (shops.Count == 0)
            {
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    Rejected("This account is not attached to any shop.", StatusCodes.Status403Forbidden));
            }

            device.FailedPinAttempts = result.FailedAttempts;
            device.LastUsedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            await _signIn.SignInAsync(HttpContext, user);

            return SignInService.LoggedIn(user, shops, pinSet: true);
        }

        if (result.Outcome == PinUnlockOutcome.WrongPin)
        {
            device.FailedPinAttempts = result.FailedAttempts;
            await _db.SaveChangesAsync(cancellationToken);

            var wrong = Rejected("Wrong PIN.", StatusCodes.Status401Unauthorized);
            wrong.Extensions["attemptsLeft"] = PinUnlockRules.MaxAttempts - result.FailedAttempts;
            return Unauthorized(wrong);
        }

        // Refused only because the stamps differ (the password changed, or
        // every device was signed out). The PIN does not unlock, but the
        // device is not revoked and the cookie stays: signing in with the
        // current password on this phone revives the PIN (Login).
        if (result.Outcome == PinUnlockOutcome.DeviceNotTrusted && found.IsStale)
        {
            return SignInWithPassword();
        }

        // LockedOut, or revoked, or the person has left: this phone is
        // finished. Revoked, never deleted, and the person signs in with
        // their password again.
        if (result.Outcome == PinUnlockOutcome.LockedOut)
        {
            device.FailedPinAttempts = result.FailedAttempts;
        }

        device.RevokedAt ??= DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _signIn.DeleteDeviceCookie(HttpContext);
        return SignInWithPassword();
    }

    // The till calls this on start-up to find out whether it is still signed
    // in. [Authorize] here turns a missing or expired cookie into the 401 set
    // up in Program.cs. No existing endpoint has this attribute.
    [Authorize]
    [AllowWhilePasswordMustChange]
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

        var shops = await _signIn.ShopsForAsync(user.Id, cancellationToken);

        return SignedIn(user, shops);
    }

    // The signed-in person changes their own password, and only their own:
    // the user comes from the cookie, never from the request body. Reachable
    // on a temporary password, because this is how that ends.
    //
    // Rate limited like login: it checks a password, so it could be used to
    // guess one.
    [Authorize]
    [AllowWhilePasswordMustChange]
    [EnableRateLimiting("login")]
    [HttpPost("change-password")]
    public async Task<ActionResult<SignedInUserResponse>> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        string? idText = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(idText, out Guid userId))
        {
            return Unauthorized(Rejected(LoginFailed, StatusCodes.Status401Unauthorized));
        }

        // Tracked on purpose: this row is about to change.
        var user = await _db.AppUsers
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive, cancellationToken);

        if (user is null)
        {
            return Unauthorized(Rejected(LoginFailed, StatusCodes.Status401Unauthorized));
        }

        // The same count as signing in: this checks a password too.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        TimeSpan? wait = _attempts.WaitFor(user.Email, now);

        if (wait is not null)
        {
            return TooManyWrongPasswords(wait.Value);
        }

        if (!_passwords.Check(user, request.CurrentPassword))
        {
            _attempts.RecordFailure(user.Email, now);
            return BadRequest(Refused("Your current password is not right. Check it and try again."));
        }

        _attempts.RecordSuccess(user.Email);

        // The one rule: at least 8 characters.
        if (request.NewPassword.Length < 8)
        {
            return BadRequest(Refused("The new password must be at least 8 characters."));
        }

        // This phone's trusted device, if it belongs to this person and is
        // still trusted. Read before the stamp changes, because trust is
        // judged against the old stamp.
        var thisPhone = await _signIn.ReadDeviceCookieAsync(HttpContext, cancellationToken);

        user.PasswordHash = _passwords.Hash(user, request.NewPassword);
        user.MustChangePassword = false;

        // Every other device with this login is signed out: a new stamp
        // kills their cookies on their next request. This phone is signed
        // back in straight after, with the new stamp, so it carries on.
        user.SecurityStamp = Guid.NewGuid();

        // This phone keeps its PIN: its device moves to the new stamp. Every
        // other phone's device still holds the old stamp, so it is dead.
        if (thisPhone is not null && thisPhone.IsTrusted && thisPhone.User.Id == user.Id)
        {
            thisPhone.Device.SecurityStamp = user.SecurityStamp.ToString();
        }

        // One save: the new password, the new stamp and this phone's device
        // land together, or none of them do.
        await _db.SaveChangesAsync(cancellationToken);

        await _signIn.SignInAsync(HttpContext, user);

        var shops = await _signIn.ShopsForAsync(user.Id, cancellationToken);

        return SignedIn(user, shops);
    }

    // Always 200: signing out when you were never signed in is not an error,
    // and the till should never have to handle a failure here.
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok();
    }

    // 429 in the same shape as the address limit's refusal, so the screen
    // shows it the same way. Says how long, never how many are left.
    private ObjectResult TooManyWrongPasswords(TimeSpan wait)
    {
        int seconds = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds));
        int minutes = Math.Max(1, (int)Math.Ceiling(wait.TotalMinutes));

        Response.Headers.RetryAfter = seconds.ToString();

        return StatusCode(
            StatusCodes.Status429TooManyRequests,
            Rejected(
                "Too many wrong passwords for this account. Wait "
                    + minutes + (minutes == 1 ? " minute" : " minutes") + " and try again.",
                StatusCodes.Status429TooManyRequests));
    }

    private UnauthorizedObjectResult SignInWithPassword()
    {
        var problem = Rejected(
            "Sign in with your email and password.", StatusCodes.Status401Unauthorized);
        problem.Extensions["signInWithPassword"] = true;
        return Unauthorized(problem);
    }

    private static SignedInUserResponse SignedIn(
        AppUser user,
        List<ShopMembershipResponse> shops)
    {
        return new SignedInUserResponse
        {
            Id = user.Id,
            FullName = user.FullName,
            Shops = shops,
            MustChangePassword = user.MustChangePassword
        };
    }

    private static ProblemDetails Refused(string detail) => new()
    {
        Title = "The password was not changed",
        Detail = detail,
        Status = StatusCodes.Status400BadRequest
    };

    private static ProblemDetails Rejected(string detail, int status) => new()
    {
        Title = "Sign in failed",
        Detail = detail,
        Status = status
    };
}
