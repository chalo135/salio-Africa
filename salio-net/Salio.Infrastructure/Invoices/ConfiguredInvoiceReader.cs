using Microsoft.Extensions.Configuration;

namespace Salio.Infrastructure.Invoices;

// The reader the API uses. It picks one from configuration:
//
//   InvoiceReader:Provider   which service reads the invoice (a name)
//   InvoiceReader:ApiKey     that service's key. NEVER in a file in the repo:
//                            user-secrets in Development, the environment
//                            variable InvoiceReader__ApiKey in production.
//
// No key: the demo reader, and the answer says IsDemo = true so the screen
// shows "demo extraction - not a real reading".
// A key: the named provider's reader. None has been built yet, so this
// refuses in words rather than pretend to have read anything.
//
// "Which reader?" is decided in ONE place, FindRealReader. Both the upload
// (ReadAsync) and the question "is a real reader switched on?" (IsReal) ask
// it, so the two can never disagree.
public class ConfiguredInvoiceReader : IInvoiceReader
{
    private readonly IConfiguration _configuration;
    private readonly FakeInvoiceReader _demo = new();

    public ConfiguredInvoiceReader(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    // True only when a real, built reader is configured: a key AND a
    // provider that has a case in FindRealReader. False for the demo reader,
    // and false for a key whose provider nobody has built (uploads then
    // answer 503). Today no real reader exists, so this is always false; it
    // turns true by itself the day one is added to the switch below.
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
            // Write a class in this folder, e.g. SomeProviderInvoiceReader :
            // IInvoiceReader, that sends the file to the provider with apiKey,
            // turns its answer into an InvoiceExtraction with IsDemo = false,
            // and throws if the provider fails (never return demo lines).
            // Then add its case here, for example:
            //
            //   case "someprovider":
            //       return new SomeProviderInvoiceReader(apiKey);

            default:
                whyNot = "InvoiceReader:Provider '" + provider
                    + "' is configured but no reader for it has been built yet";
                return null;
        }
    }
}
