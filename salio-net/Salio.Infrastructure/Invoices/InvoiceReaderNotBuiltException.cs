namespace Salio.Infrastructure.Invoices;

// A key is configured for a reader that does not exist in this code. The API
// answers 503 with this message and keeps nothing: it never falls back to the
// demo lines when someone has asked for a real reading.
public class InvoiceReaderNotBuiltException : Exception
{
    public InvoiceReaderNotBuiltException(string message) : base(message)
    {
    }
}
