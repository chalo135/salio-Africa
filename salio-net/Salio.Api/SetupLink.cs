using Salio.Infrastructure.Security;

namespace Salio.Api;

/// <summary>
/// The link a client opens to set up their shop, e.g.
/// https://salio.example.com/setup?code=K7MP29. Built here once, for both
/// the command line (SetupCodeCommand) and the admin page (AdminController).
/// The host comes from the setting "PublicBaseUrl" (decision D11).
///
/// appsettings.json ships with the placeholder https://salio.example.com.
/// A link built from that looks right and opens nothing, so the placeholder
/// counts as "not set": the admin page then uses the address it was opened
/// on, and the command line prints no link and says why.
/// </summary>
public static class SetupLink
{
    // False when PublicBaseUrl is empty, is not a web address, or is still
    // the example.com placeholder.
    public static bool IsBaseUrlSet(IConfiguration configuration)
    {
        string baseUrl = (configuration["PublicBaseUrl"] ?? "").Trim();

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        return uri.Host != "example.com" && !uri.Host.EndsWith(".example.com");
    }

    // addressOfThisRequest is where the caller reached Salio, for example
    // "https://till.shop.co.ke". It is used only when PublicBaseUrl is not
    // set. The command line has no request, so it passes nothing.
    public static string Build(IConfiguration configuration, string code, string? addressOfThisRequest = null)
    {
        string baseUrl = IsBaseUrlSet(configuration)
            ? (configuration["PublicBaseUrl"] ?? "").Trim()
            : (addressOfThisRequest ?? "");

        return baseUrl.TrimEnd('/') + "/setup?code=" + SetupCodes.Normalise(code);
    }
}
