namespace Salio.Infrastructure.Invoices;

// What kind of file an upload really is, judged by its first bytes, never by
// the name or the Content-Type header the browser sent (D10).
public static class InvoiceFile
{
    public const long MaxBytes = 10 * 1024 * 1024;

    public const string TooBigMessage = "The file is larger than 10 MB. Take the photo again at a smaller size, or send a smaller PDF.";

    public const string WrongTypeMessage = "Send a photo (PNG or JPEG) or a PDF of the invoice.";

    // ("image/png", ".png"), or nulls when it is none of the three.
    public static (string? ContentType, string? Extension) Sniff(byte[] content)
    {
        byte[] png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        byte[] jpeg = { 0xFF, 0xD8, 0xFF };
        byte[] pdf = { 0x25, 0x50, 0x44, 0x46, 0x2D }; // "%PDF-"

        if (StartsWith(content, png))
        {
            return ("image/png", ".png");
        }

        if (StartsWith(content, jpeg))
        {
            return ("image/jpeg", ".jpg");
        }

        if (StartsWith(content, pdf))
        {
            return ("application/pdf", ".pdf");
        }

        return (null, null);
    }

    private static bool StartsWith(byte[] content, byte[] signature)
    {
        if (content.Length < signature.Length)
        {
            return false;
        }

        for (int i = 0; i < signature.Length; i++)
        {
            if (content[i] != signature[i])
            {
                return false;
            }
        }

        return true;
    }
}
