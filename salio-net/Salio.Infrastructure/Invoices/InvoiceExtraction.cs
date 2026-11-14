namespace Salio.Infrastructure.Invoices;

// What a reader made of one supplier invoice. Any header field may be
// missing. Kept as it came in SupplierInvoice.RawExtractionJson.
public class InvoiceExtraction
{
    public string? SupplierName { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateOnly? InvoiceDate { get; set; }

    // The total printed on the invoice, in minor units. Null if unreadable.
    public long? TotalMinor { get; set; }

    public List<InvoiceExtractionLine> Lines { get; set; } = new();

    // True when nothing real read the file: the canned demo lines were
    // returned instead. The screen must then say "demo extraction".
    public bool IsDemo { get; set; }
}

public class InvoiceExtractionLine
{
    // The line exactly as read off the invoice.
    public string RawText { get; set; } = string.Empty;

    public string ProposedName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }

    // ONE unit, in minor units.
    public long UnitCostMinor { get; set; }
}
