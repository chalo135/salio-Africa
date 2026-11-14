using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Salio.Api.Controllers;
using Salio.Api.Dtos;
using Salio.Domain.Entities;
using Salio.Infrastructure;
using Salio.Infrastructure.Security;

namespace Salio.Api.Security;

/// <summary>
/// The one place that signs a person in and trusts their phone. Used by a
/// password login (AuthController) and by finishing shop setup
/// (SetupController), so both do exactly the same thing.
///
/// Two cookies:
///   salio.auth    the session: who is signed in. Ends after 12 hours.
///   salio_device  proof that this phone signed in with a password, so a
///                 PIN may unlock it later. Lasts 90 days.
/// </summary>
public class SignInService
{
    private const string DeviceCookie = "salio_device";

    // The device cookie is sent only to /api/auth, never with shop requests.
    private const string DeviceCookiePath = "/api/auth";

    private readonly SalioDbContext _db;
    private readonly PasswordService _passwords;
    private readonly IWebHostEnvironment _environment;

    public SignInService(SalioDbContext db, PasswordService passwords, IWebHostEnvironment environment)
    {
        _db = db;
        _passwords = passwords;
        _environment = environment;
    }

    // What the salio_device cookie proves. IsTrusted says whether the device
    // may still be used.
    //
    // IsStale: not revoked and the person still works here, but the stamps
    // differ (the password changed or every device was signed out since the
    // PIN was set). The PIN does not work, but the device is kept: the next
    // password login on this phone brings it up to date and the PIN lives on.
    public record DeviceFromCookie(TrustedDevice Device, AppUser User, bool IsTrusted, bool IsStale);

    // The person has just proved who they are with their password. Signs
    // them in and makes this phone their trusted device. Returns whether the
    // device already has a PIN: true means no PIN needs to be offered.
    public async Task<bool> SignInAndTrustDeviceAsync(
        HttpContext http,
        AppUser user,
        CancellationToken cancellationToken)
    {
        var previous = await ReadDeviceCookieAsync(http, cancellationToken);

        // This phone is already this person's device, and not revoked: keep
        // it, PIN and all. The password just proved who they are, so wrong
        // PINs are forgotten, and a stamp left behind by a password change
        // or "sign out every device" is brought up to date: the PIN lives on.
        if (previous is not null
            && previous.User.Id == user.Id
            && previous.Device.RevokedAt is null)
        {
            previous.Device.SecurityStamp = user.SecurityStamp.ToString();
            previous.Device.FailedPinAttempts = 0;
            previous.Device.LastUsedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            await SignInAsync(http, user);

            return previous.Device.PinHash is not null;
        }

        // Otherwise this phone is trusted afresh, with no PIN. A device the
        // phone held for someone else stops working.
        if (previous is not null && previous.Device.RevokedAt is null)
        {
            previous.Device.RevokedAt = DateTimeOffset.UtcNow;
        }

        // The secret goes only into the cookie. The database keeps a hash of
        // it, made by Identity's hasher like a password (locked rule 11).
        string secret = RandomNumberGenerator.GetHexString(64);

        var device = new TrustedDevice
        {
            Id = Guid.NewGuid(),
            AppUserId = user.Id,
            SecretHash = _passwords.Hash(user, secret),
            SecurityStamp = user.SecurityStamp.ToString(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        _db.TrustedDevices.Add(device);
        await _db.SaveChangesAsync(cancellationToken);

        SetDeviceCookie(http, device.Id, secret);
        await SignInAsync(http, user);

        return false;
    }

    // Writes the session cookie. The cookie carries the person, not the shop.
    public async Task SignInAsync(HttpContext http, AppUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),

            // Checked against the database on every request, so an owner can
            // sign out every device without touching passwords (rule 12).
            new(AuthController.SecurityStampClaim, user.SecurityStamp.ToString())
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

        await http.SignInAsync(
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

    // Reads salio_device ("{deviceId}.{secret}") and checks it. Null when the
    // cookie is missing, malformed, names no device, or carries the wrong
    // secret: then it proves nothing. Otherwise the device is not trusted if
    // it was revoked, the person has left, or every device was signed out
    // since (the stamps differ). The device row is tracked, so a caller can
    // change it and save.
    //
    // The browser sends this cookie only to /api/auth/..., so anywhere else
    // (for example /api/setup/shop) it is always missing.
    public async Task<DeviceFromCookie?> ReadDeviceCookieAsync(
        HttpContext http,
        CancellationToken cancellationToken)
    {
        string? cookie = http.Request.Cookies[DeviceCookie];

        if (string.IsNullOrEmpty(cookie))
        {
            return null;
        }

        string[] parts = cookie.Split('.');

        if (parts.Length != 2 || !Guid.TryParse(parts[0], out Guid deviceId) || parts[1] == "")
        {
            return null;
        }

        var device = await _db.TrustedDevices
            .FirstOrDefaultAsync(d => d.Id == deviceId, cancellationToken);

        if (device is null)
        {
            return null;
        }

        var user = await _db.AppUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == device.AppUserId, cancellationToken);

        if (user is null || !_passwords.Check(user, device.SecretHash, parts[1]))
        {
            return null;
        }

        bool usable = device.RevokedAt is null && user.IsActive;
        bool stampsMatch = device.SecurityStamp == user.SecurityStamp.ToString();

        bool isTrusted = usable && stampsMatch;
        bool isStale = usable && !stampsMatch;

        return new DeviceFromCookie(device, user, isTrusted, isStale);
    }

    // Only the device id written in the salio_device cookie, with nothing
    // checked: null when there is no cookie or it is not in the right shape.
    // Used by the "pin-unlock" rate limit in Program.cs to count tries per
    // phone. It proves nothing about the phone; ReadDeviceCookieAsync does.
    public static string? DeviceIdInCookie(HttpContext http)
    {
        string? cookie = http.Request.Cookies[DeviceCookie];

        if (string.IsNullOrEmpty(cookie))
        {
            return null;
        }

        string[] parts = cookie.Split('.');

        if (parts.Length != 2 || !Guid.TryParse(parts[0], out Guid deviceId))
        {
            return null;
        }

        return deviceId.ToString();
    }

    public void DeleteDeviceCookie(HttpContext http)
    {
        http.Response.Cookies.Delete(DeviceCookie, DeviceCookieOptions(null));
    }

    // The shops this person may open, with their role in each.
    public async Task<List<ShopMembershipResponse>> ShopsForAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await (
            from membership in _db.Memberships.AsNoTracking()
            join organization in _db.Organizations.AsNoTracking()
                on membership.OrganizationId equals organization.Id
            where membership.AppUserId == userId
                && membership.DeactivatedAt == null
            orderby organization.Name
            select new ShopMembershipResponse
            {
                OrganizationId = organization.Id,
                Name = organization.Name,
                Role = membership.Role,
                ItemWord = organization.SellableNoun,
                BusinessKind = organization.BusinessKind,
                Trade = organization.Trade
            })
            .ToListAsync(cancellationToken);
    }

    // What a password login, a PIN unlock or finishing setup sends back.
    public static LoginResponse LoggedIn(
        AppUser user,
        List<ShopMembershipResponse> shops,
        bool pinSet)
    {
        return new LoginResponse
        {
            Id = user.Id,
            FullName = user.FullName,
            Shops = shops,
            MustChangePassword = user.MustChangePassword,
            PinSet = pinSet
        };
    }

    private void SetDeviceCookie(HttpContext http, Guid deviceId, string secret)
    {
        http.Response.Cookies.Append(
            DeviceCookie,
            deviceId + "." + secret,
            DeviceCookieOptions(DateTimeOffset.UtcNow.AddDays(90)));
    }

    // Like the session cookie: HttpOnly so no script can read it, Lax so no
    // other website's POST carries it, and Secure (HTTPS only) everywhere
    // except http://localhost in development. Path /api/auth: the browser
    // sends it to the sign-in endpoints and nowhere else. 90 days when set.
    private CookieOptions DeviceCookieOptions(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = !_environment.IsDevelopment(),
        Path = DeviceCookiePath,
        Expires = expires
    };
}
