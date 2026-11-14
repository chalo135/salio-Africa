using Microsoft.EntityFrameworkCore;
using Salio.Infrastructure;
using Salio.Infrastructure.Security;

namespace Salio.Api;

/// <summary>
/// The command line way to make a setup code:
///
///     dotnet run --project Salio.Api -- new-setup-code "Kamau Motor Spares" wanjiku@example.com
///
/// Prints the code, the setup link and the expiry, and nothing else, so it
/// can be copied straight into a message to the client. The web server is
/// never started. Uses the same SetupCodeService as the admin page.
///
/// On the server, inside the API's container, which already holds the two
/// settings this needs (ConnectionStrings__Default and PublicBaseUrl):
///
///     docker compose run --rm api new-setup-code "Kamau Motor Spares" wanjiku@example.com
/// </summary>
public static class SetupCodeCommand
{
    public const string Name = "new-setup-code";

    private const string Usage =
        "Usage: dotnet run --project Salio.Api -- new-setup-code \"<shop name>\" <email>";

    // Returns the process exit code: 0 when a code was made.
    public static async Task<int> RunAsync(string[] args)
    {
        // args[0] is "new-setup-code".
        if (args.Length != 3
            || string.IsNullOrWhiteSpace(args[1])
            || !args[2].Contains('@'))
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        string shopName = args[1].Trim();
        string email = args[2].Trim();

        // The same settings the API reads (appsettings, user secrets in
        // Development), with every log switched off so only the three lines
        // below are printed. No arguments are passed on, so none of the
        // command's words are read as settings.
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.None);

        builder.Services.AddDbContext<SalioDbContext>(options =>
            options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
        builder.Services.AddScoped<SetupCodeService>();

        // Built but never run: no web server starts.
        await using var app = builder.Build();

        using var scope = app.Services.CreateScope();
        var setupCodes = scope.ServiceProvider.GetRequiredService<SetupCodeService>();

        var setupCode = await setupCodes.IssueAsync(shopName, email, CancellationToken.None);

        Console.WriteLine("Code:    " + SetupCodes.Format(setupCode.Code));

        if (SetupLink.IsBaseUrlSet(app.Configuration))
        {
            Console.WriteLine("Link:    " + SetupLink.Build(app.Configuration, setupCode.Code));
        }
        else
        {
            // A link built from the placeholder would look right and open
            // nothing. The code itself is good: it can be typed at /setup.
            Console.WriteLine("Link:    (none: PublicBaseUrl is not set)");
            Console.Error.WriteLine(
                "PublicBaseUrl is not set to this server's real address, so no link was made. "
                + "The code works: the client can type it at <your address>/setup. "
                + "To get links, set the environment variable PublicBaseUrl, e.g. https://till.yourshop.co.ke");
        }

        Console.WriteLine("Expires: " + setupCode.ExpiresAt.ToString("yyyy-MM-dd HH:mm") + " UTC");

        return 0;
    }
}
