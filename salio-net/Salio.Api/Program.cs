using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Salio.Api;
using Salio.Api.Controllers;
using Salio.Api.Security;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;
using Salio.Infrastructure;
using Salio.Infrastructure.Invoices;
using Salio.Infrastructure.Json;
using Salio.Infrastructure.Security;
using Scalar.AspNetCore;

// "new-setup-code" makes a setup code on the command line and stops. The
// web server is never started for it. See SetupCodeCommand.cs.
if (args.Length > 0 && args[0] == SetupCodeCommand.Name)
{
    Environment.ExitCode = await SetupCodeCommand.RunAsync(args);
    return;
}

// "repair-imported-costs" reverses one shop's imported opening stock, dry run
// unless --commit. The web server is never started for it either.
if (args.Length > 0 && args[0] == RepairImportedCostsCommand.Name)
{
    Environment.ExitCode = await RepairImportedCostsCommand.RunAsync(args);
    return;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<SalioDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<LedgerService>();
builder.Services.AddScoped<JournalPoster>();
builder.Services.AddScoped<SaleService>();
builder.Services.AddScoped<StockReceiptService>();
builder.Services.AddScoped<ExpenseService>();
builder.Services.AddScoped<CustomerPaymentService>();
builder.Services.AddScoped<CustomerLedger>();
builder.Services.AddScoped<StockService>();
builder.Services.AddScoped<StockPoster>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<TaxService>();

// Opening stock and stock receipts, shared by their screens, the product
// import, quick-add and the invoice reader (decision D3).
builder.Services.AddScoped<OpeningStockWriter>();
builder.Services.AddScoped<StockReceiptWriter>();

// Undo posts a mirror of an entry; it never edits or deletes one (D5).
builder.Services.AddScoped<JournalReverser>();

// Which stock a product import brought in. Reads only.
builder.Services.AddScoped<ImportedCostFinder>();
builder.Services.AddScoped<ProductImportService>();

// Supplier invoices. The reader is chosen from configuration: with no
// InvoiceReader:ApiKey it is the demo reader and says so; with a key for a
// provider nobody has built, it refuses (503). See ConfiguredInvoiceReader.
//
// Registered twice on purpose, as one object per request: the upload asks
// for "a reader" (IInvoiceReader), and GET reader-status asks the same
// object whether a real one is configured.
builder.Services.AddScoped<ConfiguredInvoiceReader>();
builder.Services.AddScoped<IInvoiceReader>(services =>
    services.GetRequiredService<ConfiguredInvoiceReader>());
builder.Services.AddScoped<SupplierInvoiceService>();

// Hashing has no state of its own, so one instance serves every request.
builder.Services.AddSingleton<PasswordService>();

// Locked rule 9: who may touch which shop.
builder.Services.AddScoped<MembershipRules>();
builder.Services.AddScoped<MembershipResolver>();
builder.Services.AddScoped<OrganizationGuard>();
builder.Services.AddScoped<UserCreator>();

// The one place setup codes are made, checked and used.
builder.Services.AddScoped<SetupCodeService>();

// Wrong passwords counted per account, in memory. One for the whole API.
builder.Services.AddSingleton<SignInAttempts>();

// Anything a controller did not expect ends here: a plain sentence and a
// short code for the client, the full error and the same code in the log.
// Never a stack trace. Built into ASP.NET Core: no package.
builder.Services.AddExceptionHandler<UnhandledErrorHandler>();

// Behind the reverse proxy (Caddy), every request reaches the API from the
// proxy's address, over plain HTTP. Caddy says who really called and how in
// two headers: X-Forwarded-For (the caller's address) and X-Forwarded-Proto
// ("https"). Reading them is what makes the rate limits count per caller
// and not per proxy, and what tells the API the request was HTTPS.
//
// Those headers are believed only when the request comes from this machine
// or from a private network address, and only the last hop is read
// (ForwardLimit = 1). Private ranges are trusted as a whole because the
// proxy is either a Caddy container on Salio's own Docker network or a
// Caddy on the server reaching the container through Docker's bridge: a
// private address either way, and not known until the containers start.
// That is acceptable ONLY because the API's port is never published to the
// internet (docker-compose.yml publishes it on 127.0.0.1), so nothing but
// the proxy and Salio's own containers can reach it. If the API port is
// ever opened to the public, this trust must be narrowed first: a caller
// who reaches the API directly from a private address could then choose
// their own X-Forwarded-For.
//
// Not in Development: there is no proxy there.
if (!builder.Environment.IsDevelopment())
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;

        // This machine (127.0.0.1 and ::1) is trusted already, by default.
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.0.0.0/8"));
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("172.16.0.0/12"));
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("192.168.0.0/16"));

        // The same idea in IPv6: private ("unique local") addresses.
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("fc00::/7"));
    });
}

// Signing a person in and trusting their phone: shared by a password login
// and by finishing shop setup, so both behave the same.
builder.Services.AddScoped<SignInService>();

// The lock on /api/admin: reads IsPlatformAdmin fresh on every request.
builder.Services.AddScoped<PlatformAdminFilter>();

// The till is a React app, not a browser form, so a failed check must come
// back as a status code it can read. Never an HTML login page.
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "salio.auth";

        // JavaScript cannot read it, so a script injected into the till
        // cannot steal the login.
        options.Cookie.HttpOnly = true;

        // Lax: the browser sends it with requests made from Salio's own
        // pages, and never with a POST or PUT started by another website,
        // so another site cannot act with someone's login. In production
        // the till and the API are one address behind Caddy. In
        // development localhost:5173 calling localhost:5077 still counts
        // as the same site, because ports are not part of a "site".
        // Never None: nothing on another site needs this cookie.
        options.Cookie.SameSite = SameSiteMode.Lax;

        // Sent with every request to this address.
        options.Cookie.Path = "/";

        // http://localhost has no certificate, so Secure would stop the
        // cookie being set at all in development. Required everywhere else.
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.None
            : CookieSecurePolicy.Always;

        // Locked rule 12: a login lasts a working day, not forever. Sliding
        // is off, so 12 hours means 12 hours from signing in, however busy
        // the till has been.
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
        options.SlidingExpiration = false;

        // Status codes instead of redirects, for both "not signed in" and
        // "signed in but not allowed".
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };

        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };

        // Runs on every request that carries a cookie. It costs one small
        // query, and it is what makes "sign out every device" possible
        // (locked rule 12): change the stamp in the database and every
        // cookie holding the old one dies on its next request.
        options.Events.OnValidatePrincipal = async context =>
        {
            var principal = context.Principal;

            if (principal is null)
            {
                context.RejectPrincipal();
                return;
            }

            if (!OrganizationGuard.TryGetUserId(principal, out Guid userId))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            string? stampText = principal.FindFirstValue(AuthController.SecurityStampClaim);

            if (!Guid.TryParse(stampText, out Guid stamp))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            var db = context.HttpContext.RequestServices.GetRequiredService<SalioDbContext>();

            var user = await db.AppUsers
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);

            // Deleted, deactivated, or signed out everywhere since this
            // cookie was issued.
            if (user is null || !user.IsActive || user.SecurityStamp != stamp)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            // Still on a temporary password. PasswordChangeGate reads this
            // and lets the request reach only /me and /change-password.
            if (user.MustChangePassword)
            {
                context.HttpContext.Items[PasswordChangeGate.MustChangeKey] = true;
            }
        };
    });

builder.Services.AddAuthorization();

// Brute-force protection for signing in. Built into ASP.NET Core: no package.
//
// Every limit here counts per caller address. Behind Caddy that is the
// address in X-Forwarded-For (see ForwardedHeadersOptions above); without
// that, every caller would share the proxy's address and one bucket.
//
// One address is not one person. Staff phones behind a shop's router share
// an address, and on a mobile network unrelated shops can share one too.
// So these limits are loose enough for a busy shop, and the tight limits
// sit elsewhere: wrong passwords are counted per account (SignInAttempts),
// and wrong PINs per phone (PinUnlockRules: five and the phone is revoked).
builder.Services.AddRateLimiter(options =>
{
    // Password sign-in and change-password. Two limits chained, both must
    // allow the try: 10 a minute and 100 an hour per address. Enough for
    // every person in a 20-person shop to sign in on opening day with a few
    // mistypes; far too few to guess passwords across many accounts. One
    // account gets at most 10 wrong passwords per 10 minutes from anywhere.
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.Get(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => RateLimiter.CreateChained(
                new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0
                }),
                new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 100,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0
                }))));

    // Unlocking with a PIN. The till locks itself after a minute out of
    // sight, so a shop unlocks all day long: this must never share the
    // password limit, or a busy shop locks itself out of its own till.
    // Counted per address AND per phone (the device id in the salio_device
    // cookie): 10 tries a minute for each phone. A real phone never gets
    // near it, because five wrong PINs revoke the device. Requests with no
    // device cookie share one bucket per address; they are refused before
    // any PIN is checked.
    options.AddPolicy("pin-unlock", httpContext =>
    {
        string address = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        string device = SignInService.DeviceIdInCookie(httpContext) ?? "no-device";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: address + "|" + device,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });

    // Every /api/admin request: 60 per address per 10 minutes. Decision D6
    // said 20; the lead raised it to 60 so the admin is not locked out of
    // their own page while working. Rejected with a bare 404, see below.
    options.AddPolicy("admin", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0
            }));

    // Setting up a shop: checking a code and creating the shop share one
    // allowance of 30 tries per address per hour. A code is six characters
    // from 30 (729 million codes), so this still makes guessing one
    // hopeless. It was 10; a new owner correcting a few fields on a shared
    // mobile address could use that up and be told to wait an hour.
    options.AddPolicy("setup-redeem", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromHours(1),
                QueueLimit = 0
            }));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Same ProblemDetails shape as every other refusal, so the screen shows
    // the message as it already does. The words depend on which limit was
    // hit: setup has its own, everything else is sign-in.
    options.OnRejected = async (context, cancellationToken) =>
    {
        string? policy = context.HttpContext.GetEndpoint()?
            .Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;

        // The admin area answers a tripped limit with the same bare 404 as
        // an unknown path: a 429 would prove /api/admin is real.
        if (policy == "admin")
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // Who was turned away, so "I cannot sign in" can be answered from
        // the log. The address and the path only: never what was typed.
        var limitLogger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Salio.Api.RateLimit");

        limitLogger.LogWarning(
            "Rate limit {Policy} refused {Method} {Path} from {Address}.",
            policy,
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path.Value,
            context.HttpContext.Connection.RemoteIpAddress);

        ProblemDetails problem;

        if (policy == "pin-unlock")
        {
            context.HttpContext.Response.Headers.RetryAfter = "60";

            problem = new ProblemDetails
            {
                Title = "Sign in failed",
                Detail = "Too many tries. Wait a minute and try again.",
                Status = StatusCodes.Status429TooManyRequests
            };
        }
        else if (policy == "setup-redeem")
        {
            context.HttpContext.Response.Headers.RetryAfter = "3600";

            problem = new ProblemDetails
            {
                Title = "The shop was not set up",
                Detail = "Too many tries. Wait an hour and try again, or ask for a new code.",
                Status = StatusCodes.Status429TooManyRequests
            };
        }
        else
        {
            // The login limit is two limits chained: 10 a minute and 100 an
            // hour. The limiter knows which one tripped and how long is left;
            // use that when it says, and a minute when it does not.
            int retrySeconds = 60;

            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
            {
                retrySeconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            }

            context.HttpContext.Response.Headers.RetryAfter = retrySeconds.ToString();

            problem = new ProblemDetails
            {
                Title = "Sign in failed",
                Detail = retrySeconds > 60
                    ? "Too many sign-in attempts. Wait up to an hour and try again."
                    : "Too many sign-in attempts. Wait a minute and try again.",
                Status = StatusCodes.Status429TooManyRequests
            };
        }

        await context.HttpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
    };
});

// Which other web addresses may call the API with the login cookie. Read
// from the setting "Cors:AllowedOrigins" (addresses separated by ";").
//
// Development sets it to http://localhost:5173 (appsettings.Development.json):
// the till runs on a different port from the API there, and the browser
// blocks those calls unless the API says the origin is welcome.
//
// Everywhere else it is empty, and empty means no cross-origin access at
// all: in production Caddy serves the till and /api on one address, so the
// browser never needs permission.
//
// AllowCredentials is what lets the login cookie travel. It is only ever
// given to named origins; CorsOrigins.Parse throws "*" away, because any
// origin plus credentials would let any website spend someone's login.
string[] corsOrigins = CorsOrigins.Parse(builder.Configuration[CorsOrigins.SettingName]);

builder.Services.AddCors(options =>
    options.AddPolicy("SalioTill", policy => policy
        .WithOrigins(corsOrigins)
        .AllowCredentials()
        .AllowAnyHeader()
        .AllowAnyMethod()));

// Every DateTimeOffset a client sends is turned into UTC as it is read.
// Npgsql refuses to save one with an offset such as +03:00, which used to be
// a 500. Fixed here once, not in each caller.
//
// AllowInputFormatterExceptionMessages off: a body that is not valid JSON,
// or holds text where a number belongs, is still refused with a 400 that
// names the field, but in plain words ("The input was not valid.") and
// without the names of Salio's C# types, which the default message shows.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new UtcDateTimeOffsetConverter());
        options.AllowInputFormatterExceptionMessages = false;
    });
builder.Services.AddOpenApi();

var app = builder.Build();

// FIRST, before anything reads the caller's address or asks "is this
// HTTPS?": rewrite both from what the proxy says. Not in Development,
// where there is no proxy and the options above were not set.
if (!app.Environment.IsDevelopment())
{
    app.UseForwardedHeaders();
}

// Catches whatever goes wrong below it and hands it to UnhandledErrorHandler.
// In every environment, Development included, so the till shows the same
// thing here as it will in the shop; the full error is in the console log.
app.UseExceptionHandler(_ => { });

if (app.Environment.IsDevelopment())
{
    // The API pages (Scalar) and the OpenAPI file describe every endpoint.
    // Never mapped anywhere else: outside Development they answer 404.
    app.MapOpenApi();
    app.MapScalarApiReference();
    await SetUpDevelopmentDatabaseAsync(app);

    // HTTP to HTTPS on the developer's machine only. In production Caddy
    // owns HTTPS: it redirects http:// itself and sends the HSTS header,
    // and the API only ever hears plain HTTP from Caddy on a private
    // address. Redirecting here as well could only cause a redirect loop
    // or turn the deploy's plain-HTTP health poll into a 307.
    app.UseHttpsRedirection();
}
else
{
    WarnAboutProductionSettings(app, corsOrigins);
}

// Only when an origin is named. With none, no CORS header is ever sent.
if (corsOrigins.Length > 0)
{
    app.UseCors("SalioTill");
}

// Reads the cookie and works out who the caller is. No existing endpoint
// requires it, so nothing that worked yesterday changes.
app.UseAuthentication();

// A person on a temporary password can reach /me and /change-password and
// nothing else. Enforced here, once, rather than in every controller.
app.UsePasswordChangeGate();

app.UseAuthorization();

// After CORS, so the browser can read a 429 and show its message.
app.UseRateLimiter();

app.MapControllers();

app.Run();


// Runs once at startup, outside Development. Says loudly, in the log, when
// a setting that production needs is missing or still a placeholder. It
// never stops the API: a till that is up with one feature off beats a till
// that will not start.
static void WarnAboutProductionSettings(WebApplication app, string[] corsOrigins)
{
    if (!SetupLink.IsBaseUrlSet(app.Configuration))
    {
        app.Logger.LogError(
            "PublicBaseUrl is not set to this server's real address (it is '{PublicBaseUrl}'). "
            + "A setup link made on the command line would point at the wrong place, so new-setup-code "
            + "prints no link until it is set. Set the environment variable PublicBaseUrl, "
            + "for example https://till.yourshop.co.ke. The admin page uses the address it was opened on.",
            app.Configuration["PublicBaseUrl"]);
    }

    if (string.IsNullOrWhiteSpace(app.Configuration["InvoiceStorage:Path"]))
    {
        app.Logger.LogWarning(
            "InvoiceStorage:Path is not set, so supplier invoice uploads answer 503. "
            + "Everything else works. Set the environment variable InvoiceStorage__Path to a folder that is kept.");
    }

    string allowedHosts = (app.Configuration["AllowedHosts"] ?? "").Trim();

    if (allowedHosts == "" || allowedHosts == "*")
    {
        app.Logger.LogWarning(
            "AllowedHosts is not set, so the API answers requests for any host name. "
            + "Set the environment variable AllowedHosts to this server's domain.");
    }

    if (corsOrigins.Length > 0)
    {
        app.Logger.LogWarning(
            "Cors:AllowedOrigins lets these other addresses call the API with the login cookie: {Origins}. "
            + "Leave it empty when the till and the API share one address.",
            string.Join("; ", corsOrigins));
    }
}


// Runs once at startup, in Development only.
// Applies any pending migrations, then makes sure there is one organisation
// with a chart of accounts to post against.
static async Task SetUpDevelopmentDatabaseAsync(WebApplication app)
{
    // The demo seed writes stock and books it against Owner's Equity. It must
    // never reach a real shop's books, whoever calls this.
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "The development database setup only runs in Development.");
    }

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<SalioDbContext>();

    await db.Database.MigrateAsync();

    // A fixed id, so development data stays the same between runs.
    var organizationId = new Guid("00000000-0000-0000-0000-000000000001");

    bool organizationExists = await db.Organizations
        .AnyAsync(o => o.Id == organizationId);

    if (!organizationExists)
    {
        db.Organizations.Add(new Organization
        {
            Id = organizationId,
            Name = "Salio Demo Shop",
            Currency = "KES",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync();
    }

    await ChartOfAccountsSeeder.SeedAsync(db, organizationId, CancellationToken.None);
    await TaxRateSeeder.SeedAsync(db, organizationId, CancellationToken.None);

    // A shelf of motorcycle spares with opening stock, so the till has
    // something to sell. OFF unless the setting SeedDemoProducts is true:
    // otherwise the demo parts came back on every start, even after they
    // were wiped. A missing setting reads as false. The chart of accounts
    // and tax rate above always seed, because a shop needs them to record
    // anything.
    bool seedDemoProducts = app.Configuration.GetValue<bool>("SeedDemoProducts");

    if (seedDemoProducts)
    {
        // The posters come from the same scope, so they share this db and
        // its transaction.
        var stockPoster = scope.ServiceProvider.GetRequiredService<StockPoster>();
        var journalPoster = scope.ServiceProvider.GetRequiredService<JournalPoster>();
        await DemoProductSeeder.SeedAsync(db, stockPoster, journalPoster, organizationId, CancellationToken.None);
    }
    else
    {
        app.Logger.LogInformation(
            "SeedDemoProducts is not true, so the demo products were not seeded.");
    }

    await SeedTestUserAsync(app, db, organizationId);
}


// One test user for Development, and only if a password was supplied out of
// band. There is no default password and nothing is written to any file: with
// no secret set, the till simply has no user, which is safer than a password
// that ships in the repository.
static async Task SeedTestUserAsync(WebApplication app, SalioDbContext db, Guid organizationId)
{
    const string email = "test@salio.local";

    string? password = app.Configuration["SeedTestUserPassword"];

    if (string.IsNullOrWhiteSpace(password))
    {
        app.Logger.LogWarning(
            "SeedTestUserPassword is not set, so the test user {Email} was skipped. " +
            "Set it with: dotnet user-secrets set \"SeedTestUserPassword\" \"<password>\" --project Salio.Api",
            email);
        return;
    }

    // Per email, like the product seeder: a second start inserts nothing.
    bool userExists = await db.AppUsers.AnyAsync(u => u.Email.ToLower() == email);

    if (userExists)
    {
        return;
    }

    var passwords = app.Services.GetRequiredService<PasswordService>();
    var userCreator = new UserCreator(db, passwords);

    await userCreator.CreateAsync(
        organizationId,
        "Test Owner",
        email,
        password,
        MembershipRole.Owner,
        CancellationToken.None);

    app.Logger.LogInformation("Seeded the Development test user {Email} as Owner.", email);
}
