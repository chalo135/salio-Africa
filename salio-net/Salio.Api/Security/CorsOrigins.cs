namespace Salio.Api.Security;

/// <summary>
/// Reads the setting "Cors:AllowedOrigins": the web addresses, other than
/// the API's own, whose pages may call the API with the login cookie.
/// One string, addresses separated by ";", for example
/// "http://localhost:5173;https://till.example.com".
///
/// Empty means none, which is right in production: Caddy serves the till
/// and /api on one address, so no other origin ever needs access.
/// </summary>
public static class CorsOrigins
{
    public const string SettingName = "Cors:AllowedOrigins";

    public static string[] Parse(string? setting)
    {
        List<string> origins = new();

        foreach (string part in (setting ?? "").Split(';'))
        {
            // A browser sends an origin with no slash on the end.
            string origin = part.Trim().TrimEnd('/');

            if (origin == "")
            {
                continue;
            }

            // "*" would let any website spend a signed-in person's login.
            // It is never accepted, whatever the setting says. Neither is
            // anything that is not a plain http or https address.
            if (origin.Contains('*')
                || !Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                continue;
            }

            if (!origins.Contains(origin, StringComparer.OrdinalIgnoreCase))
            {
                origins.Add(origin);
            }
        }

        return origins.ToArray();
    }
}
