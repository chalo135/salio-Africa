namespace Salio.Infrastructure.Invoices;

// A real reader was asked to read an invoice and could not: the photo was
// unreadable, the provider refused or was busy, or its answer made no sense.
// The message is written for the shop owner. The API answers 503 with it and
// keeps nothing. Like InvoiceReaderNotBuiltException, it never falls back to
// the demo lines.
public class InvoiceReadFailedException : Exception
{
    public InvoiceReadFailedException(string message) : base(message)
    {
    }
}
