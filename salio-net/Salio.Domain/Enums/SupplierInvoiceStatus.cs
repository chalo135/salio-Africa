namespace Salio.Domain.Enums
{
    // Stored as a number. New values go at the end.
    public enum SupplierInvoiceStatus
    {
        // Uploaded and read, nobody has checked the lines yet.
        Pending,

        // The owner has checked and corrected the lines.
        Reviewed,

        // Turned into a stock receipt. Done.
        Committed,

        // Thrown away. Nothing was posted.
        Discarded
    }
}
