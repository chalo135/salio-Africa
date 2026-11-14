using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Salio.Api.Dtos;
using Salio.Api.Security;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Infrastructure;
using Salio.Infrastructure.Security;

namespace Salio.Api.Controllers;

/// <summary>
/// Making a shop. Nobody is signed in yet when a shop is made, so this is the
/// one controller that allows anonymous callers.
///
/// Locked rule 14: a client sets up their own shop, but only with a setup
/// code the developer issued to them. No code, no shop. The developer never
/// sees the owner's password: the owner types it, and it is stored only as
/// a hash.
///
/// Codes are made only by SetupCodeService, from the command line
/// (new-setup-code) or the admin page. POST organization answers 404
/// outside Development.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/setup")]
public class SetupController : ControllerBase
{
    private readonly SalioDbContext _db;
    private readonly UserCreator _userCreator;
    private readonly SetupCodeService _setupCodes;
    private readonly IWebHostEnvironment _environment;
    private readonly SignInService _signIn;
    private readonly ILogger<SetupController> _logger;

    public SetupController(
        SalioDbContext db,
        UserCreator userCreator,
        SetupCodeService setupCodes,
        IWebHostEnvironment environment,
        SignInService signIn,
        ILogger<SetupController> logger)
    {
        _db = db;
        _userCreator = userCreator;
        _setupCodes = setupCodes;
        _environment = environment;
        _signIn = signIn;
        _logger = logger;
    }

    // One message for an unknown, expired, revoked and used code alike, so
    // trying codes reveals nothing about which ones exist.
    private const string CodeRefused =
        "That setup code does not work. Check it, or ask for a new one.";

    // The first step of setup: is this code good? Returns only the shop name
    // it was issued for. Shares the "setup-redeem" limit with POST shop.
    [EnableRateLimiting("setup-redeem")]
    [HttpPost("check-code")]
    public async Task<ActionResult<CheckSetupCodeResponse>> CheckCode(
        CheckSetupCodeRequest request,
        CancellationToken cancellationToken)
    {
        var setupCode = await _setupCodes.FindRedeemableAsync(
            request.Code, DateTimeOffset.UtcNow, cancellationToken);

        if (setupCode is null)
        {
            return BadRequest(Refused(CodeRefused));
        }

        return new CheckSetupCodeResponse { ShopName = setupCode.ShopName };
    }

    // A stranger setting up their own shop, holding a code the developer
    // issued. Rate limited: a code is guessable in principle.
    [EnableRateLimiting("setup-redeem")]
    [HttpPost("shop")]
    public async Task<ActionResult<SetUpShopResponse>> SetUpShop(
        SetUpShopRequest request,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        var setupCode = await _setupCodes.FindRedeemableAsync(request.SetupCode, now, cancellationToken);

        if (setupCode is null)
        {
            return BadRequest(Refused(CodeRefused));
        }

        string shopName = request.ShopName.Trim();
        string businessKind = request.BusinessKind.Trim();
        string trade = request.Trade.Trim();
        string sellableNoun = request.SellableNoun.Trim();
        string fullName = request.OwnerFullName.Trim();
        string email = request.OwnerEmail.Trim();

        // Services does not switch stock off. It only sets wording and which
        // screens come first, so both kinds are made the same way here.
        if (businessKind != "Sales" && businessKind != "Services")
        {
            return BadRequest(Refused("Choose whether the business mainly sells goods or services."));
        }

        if (trade == "" || trade.Length > 120)
        {
            return BadRequest(Refused("Say what kind of business this is, in 120 characters or fewer."));
        }

        // The wizard works this word out from the trade. Checked here, never
        // worked out again (decision D13).
        if (sellableNoun == "" || sellableNoun.Length > 30)
        {
            return BadRequest(Refused("The word for what the shop sells must be 1 to 30 characters."));
        }

        if (shopName == "" || fullName == "" || !email.Contains('@'))
        {
            return BadRequest(Refused("Fill in the shop's name, your name and your email."));
        }

        if (request.OwnerPassword.Length < 8)
        {
            return BadRequest(Refused("Your password must be at least 8 characters."));
        }

        if (request.TaxPosition != TaxPosition.TurnoverTax
            && request.TaxPosition != TaxPosition.VatRegistered
            && request.TaxPosition != TaxPosition.NotSure)
        {
            return BadRequest(Refused("Choose Turnover Tax, VAT registered, or not sure."));
        }

        // Compared without case, the way login finds an account.
        string emailLower = email.ToLower();

        bool emailTaken = await _db.AppUsers
            .AnyAsync(u => u.Email.ToLower() == emailLower, cancellationToken);

        if (emailTaken)
        {
            return BadRequest(Refused(
                email + " already has a Salio account. Sign in with it instead. "
                + "Your setup code has not been used."));
        }

        var organization = new Organization
        {
            Id = Guid.NewGuid(),
            Name = shopName,
            Currency = "KES",
            CreatedAt = now,
            BusinessKind = businessKind,
            Trade = trade,
            SellableNoun = sellableNoun,
            TaxPosition = request.TaxPosition
        };

        // The code, the shop, its books, its tax rate, the owner and their
        // membership land together, or nothing does.
        using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        AppUser owner;

        try
        {
            // The shop row goes in first, because the code points at it.
            _db.Organizations.Add(organization);
            await _db.SaveChangesAsync(cancellationToken);

            // Then the code is claimed, only if still usable: of two people
            // using one code at the same moment, exactly one succeeds. The
            // loser's shop row is rolled back with the transaction.
            bool claimed = await _setupCodes.ClaimAsync(setupCode.Id, organization.Id, now, cancellationToken);

            if (!claimed)
            {
                await transaction.RollbackAsync(cancellationToken);
                return BadRequest(Refused(CodeRefused));
            }

            await ChartOfAccountsSeeder.SeedAsync(_db, organization.Id, cancellationToken);

            // A VAT-registered business does not pay Turnover Tax, so it gets
            // no Turnover Tax rate. "Not sure" gets it: below the VAT
            // threshold Turnover Tax is the usual case, and the answer is
            // kept on the shop so it can be asked again.
            if (request.TaxPosition != TaxPosition.VatRegistered)
            {
                await TaxRateSeeder.SeedAsync(_db, organization.Id, cancellationToken);
            }

            // Their own password, so no MustChangePassword.
            owner = await _userCreator.CreateAsync(
                organization.Id,
                fullName,
                email,
                request.OwnerPassword,
                MembershipRole.Owner,
                cancellationToken,
                mustChangePassword: false);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // An email taken in the moment since the check above. The code
            // goes back to unused with everything else.
            await transaction.RollbackAsync(cancellationToken);
            return BadRequest(Refused(ex.Message));
        }

        // The owner typed their password a moment ago, so they are signed in
        // now and this phone is trusted, exactly as a password login does it
        // (the same SignInService). They are not asked to sign in again.
        LoginResponse? login = null;

        try
        {
            bool pinSet = await _signIn.SignInAndTrustDeviceAsync(HttpContext, owner, cancellationToken);
            var shops = await _signIn.ShopsForAsync(owner.Id, cancellationToken);
            login = SignInService.LoggedIn(owner, shops, pinSet);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The shop is made and stays made. Login stays null, and the
            // screen signs in with /api/auth/login the old way.
            _logger.LogWarning(ex, "Shop {OrganizationId} was set up, but signing the owner in failed.", organization.Id);
        }

        return new SetUpShopResponse
        {
            OrganizationId = organization.Id,
            ShopName = organization.Name,
            OwnerEmail = email,
            Login = login
        };
    }

    // DEVELOPMENT ONLY. The old way: a shop made by the developer with no
    // code, kept for testing. 404 anywhere else.
    [HttpPost("organization")]
    public async Task<ActionResult<CreateOrganizationResponse>> CreateOrganization(
        CreateOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var organization = new Organization
        {
            Id = Guid.NewGuid(),
            Name = request.ShopName,
            Currency = "KES",
            CreatedAt = DateTimeOffset.UtcNow,
            BusinessKind = "Sales",
            SellableNoun = "Parts"
        };

        // A shop, its books, its tax rate and its first Owner arrive together
        // or not at all. Half a shop would be worse than none: no chart of
        // accounts means the till cannot sell.
        using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        AppUser owner;

        try
        {
            _db.Organizations.Add(organization);
            await _db.SaveChangesAsync(cancellationToken);

            await ChartOfAccountsSeeder.SeedAsync(_db, organization.Id, cancellationToken);
            await TaxRateSeeder.SeedAsync(_db, organization.Id, cancellationToken);

            owner = await _userCreator.CreateAsync(
                organization.Id,
                request.OwnerFullName,
                request.OwnerEmail,
                request.OwnerPassword,
                MembershipRole.Owner,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // A short password or an email already in use. Nothing is left
            // behind: no shop, no accounts, no tax rate.
            await transaction.RollbackAsync(cancellationToken);

            return BadRequest(new ProblemDetails
            {
                Title = "The shop was not created",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }

        int accountsCreated = await _db.Accounts
            .CountAsync(a => a.OrganizationId == organization.Id, cancellationToken);

        return new CreateOrganizationResponse
        {
            OrganizationId = organization.Id,
            ShopName = organization.Name,
            OwnerId = owner.Id,
            OwnerEmail = owner.Email,
            AccountsCreated = accountsCreated
        };
    }

    private static ProblemDetails Refused(string detail) => new()
    {
        Title = "The shop was not set up",
        Detail = detail,
        Status = StatusCodes.Status400BadRequest
    };
}
