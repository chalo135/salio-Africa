namespace Salio.Infrastructure.Invoices;

// Reads a photo or PDF of a supplier invoice into proposed lines. Reading
// writes nothing: the owner checks the lines, and only a commit posts them.
public interface IInvoiceReader
{
    // contentType is what the file really is (checked from its first bytes):
    // "image/png", "image/jpeg" or "application/pdf".
    Task<InvoiceExtraction> ReadAsync(Stream image, string contentType, CancellationToken cancellationToken);
}
