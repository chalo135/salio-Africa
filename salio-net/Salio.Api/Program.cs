using Microsoft.EntityFrameworkCore;
using Salio.Domain.Entities;
using Salio.Domain.Services;
using Salio.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<SalioDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<LedgerService>();
builder.Services.AddScoped<JournalPoster>();
builder.Services.AddScoped<SaleService>();
builder.Services.AddScoped<StockService>();
builder.Services.AddScoped<StockPoster>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<TaxService>();

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
}
