using Microsoft.Extensions.Configuration;

namespace Salio.Infrastructure.Invoices;

// The reader the API uses. It picks one from configuration:
//
//   InvoiceReader:Provider   which service reads the invoice (a name)
//   InvoiceReader:ApiKey     that service's key. NEVER in a file in the repo:
//                            user-secrets in Development, the environment
//                            variable InvoiceReader__ApiKey in production.
//   InvoiceReader:Model      optional; which model the provider uses.
//
// No key: the demo reader, and the answer says IsDemo = true so the screen
// shows "demo extraction - not a real reading".
// A key with Provider "anthropic": ClaudeInvoiceReader.
// A key for any other provider: refuses in words rather than pretend to have
// read anything.
//
// "Which reader?" is decided in ONE place, FindRealReader. Both the upload
// (ReadAsync) and the question "is a real reader switched on?" (IsReal) ask
// it, so the two can never disagree.
public class ConfiguredInvoiceReader : IInvoiceReader
{
    // The name of the HttpClient the API registers for the real reader, so
    // its connections are pooled and its timeout is set in one place.
    public const string HttpClientName = "InvoiceReader";

    private readonly IConfiguration _configuration;
    private readonly Func<HttpClient> _createHttpClient;
    private readonly FakeInvoiceReader _demo = new();

    // For tests and tools. A plain HttpClient is made only if a real reader
    // actually sends something.
    public ConfiguredInvoiceReader(IConfiguration configuration)
        : this(configuration, () => new HttpClient())
    {
    }

    // The API passes the client from IHttpClientFactory here (Program.cs).
    public ConfiguredInvoiceReader(IConfiguration configuration, Func<HttpClient> createHttpClient)
    {
        _configuration = configuration;
        _createHttpClient = createHttpClient;
    }

    // True only when a real, built reader is configured: a key AND a
    // provider that has a case below. False for the demo reader, and false
    // for a key whose provider nobody has built (uploads then answer 503).
    // Turns true by itself once InvoiceReader:Provider is "anthropic" and
    // InvoiceReader:ApiKey is set.
    public bool IsReal => FindRealReader(out _) is not null;

    public async Task<InvoiceExtraction> ReadAsync(Stream image, string contentType, CancellationToken cancellationToken)
    {
        IInvoiceReader? real = FindRealReader(out string? whyNot);

        if (real is not null)
        {
            return await real.ReadAsync(image, contentType, cancellationToken);
        }

        // A key was given, but there is no reader to use it with.
        if (whyNot is not null)
        {
            throw new InvoiceReaderNotBuiltException(whyNot);
        }

        // No key at all: the demo reader, and it says so.
        InvoiceExtraction demo = await _demo.ReadAsync(image, contentType, cancellationToken);
        demo.IsDemo = true;
        return demo;
    }

    // The one place that decides which reader is in use.
    //   a real reader        -> returns it, whyNot is null
    //   no key (the demo)    -> returns null, whyNot is null
    //   a key but no reader  -> returns null, whyNot says what is wrong
    private IInvoiceReader? FindRealReader(out string? whyNot)
    {
        whyNot = null;

        string provider = (_configuration["InvoiceReader:Provider"] ?? "").Trim();
        string apiKey = (_configuration["InvoiceReader:ApiKey"] ?? "").Trim();

        if (apiKey.Length == 0)
        {
            return null;
        }

        if (provider.Length == 0)
        {
            whyNot = "InvoiceReader:ApiKey is set but InvoiceReader:Provider is empty. "
                + "Name the provider, or remove the key to use the demo reader.";
            return null;
        }

        switch (provider.ToLowerInvariant())
        {
            // ---- A REAL PROVIDER PLUGS IN HERE. ----
            // Each one is a class in this folder that sends the file to the
            // provider with apiKey, turns its answer into an InvoiceExtraction
            // with IsDemo = false, and throws if the provider fails (never
            // returns demo lines).

            // Claude, through Anthropic's Messages API.
            case "anthropic":
            case "claude":
                return new ClaudeInvoiceReader(_createHttpClient, apiKey, _configuration["InvoiceReader:Model"]);

            default:
                whyNot = "InvoiceReader:Provider '" + provider
                    + "' is configured but no reader for it has been built yet";
                return null;
        }
    }
}
