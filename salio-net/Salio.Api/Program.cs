using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Salio.Api.Controllers;
using Salio.Api.Security;
using Salio.Domain.Entities;
using Salio.Domain.Enums;
using Salio.Domain.Services;
using Salio.Infrastructure;
using Salio.Infrastructure.Security;
using Scalar.AspNetCore;

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

// Hashing has no state of its own, so one instance serves every request.
builder.Services.AddSingleton<PasswordService>();

// Locked rule 9: who may touch which shop.
builder.Services.AddScoped<MembershipRules>();
builder.Services.AddScoped<MembershipResolver>();
builder.Services.AddScoped<OrganizationGuard>();
builder.Services.AddScoped<UserCreator>();

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

        // Ports are not part of a "site", so localhost:5173 calling
        // localhost:5077 counts as same-site and Lax is enough in
        // development. A deployed till on another domain needs None,
        // which also requires Secure.
        options.Cookie.SameSite = SameSiteMode.Lax;

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
            }
        };
    });

builder.Services.AddAuthorization();

// Brute-force protection for the login endpoint only. Five attempts a minute
// from one address is plenty for a person who mistyped, and far too few to
// guess a password. Built into ASP.NET Core: no package.
//
// Partitioned by IP address. Behind a reverse proxy every request would share
// the proxy's address, so production will need forwarded headers first.
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Same ProblemDetails shape as every other refusal, so the till's login
    // screen shows the message as it already does.
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";

        await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Title = "Sign in failed",
            Detail = "Too many sign-in attempts. Wait a minute and try again.",
            Status = StatusCodes.Status429TooManyRequests
        }, cancellationToken);
    };
});

// DEVELOPMENT ONLY. The till runs on a different port from the API, and the
// browser blocks those calls unless the API says the origin is welcome.
// Tighten this before deployment: a deployed API must not accept any header
// and any method from a hard-coded localhost origin.
//
// AllowCredentials is what lets the login cookie travel at all. It is only
// legal with a named origin: the browser refuses AllowAnyOrigin together
// with credentials, because that would let any site spend someone's login.
builder.Services.AddCors(options =>
    options.AddPolicy("SalioDevTill", policy => policy
        .WithOrigins("http://localhost:5173")
        .AllowCredentials()
        .AllowAnyHeader()
        .AllowAnyMethod()));

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
    await SetUpDevelopmentDatabaseAsync(app);
}

app.UseHttpsRedirection();

app.UseCors("SalioDevTill");

// Reads the cookie and works out who the caller is. No existing endpoint
// requires it, so nothing that worked yesterday changes.
app.UseAuthentication();
app.UseAuthorization();

// After CORS, so the browser can read a 429 and show its message.
app.UseRateLimiter();

app.MapControllers();

app.Run();


// Runs once at startup, in Development only.
// Applies any pending migrations, then makes sure there is one organisation
// with a chart of accounts to post against.
static async Task SetUpDevelopmentDatabaseAsync(WebApplication app)
{
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
    // something to sell. The posters come from the same scope, so they share
    // this db and its transaction.
    var stockPoster = scope.ServiceProvider.GetRequiredService<StockPoster>();
    var journalPoster = scope.ServiceProvider.GetRequiredService<JournalPoster>();
    await DemoProductSeeder.SeedAsync(db, stockPoster, journalPoster, organizationId, CancellationToken.None);

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
