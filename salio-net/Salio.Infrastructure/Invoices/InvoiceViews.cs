using System.Text.Json.Serialization;
using Salio.Domain.Enums;

namespace Salio.Infrastructure.Invoices;

// The shapes the supplier-invoice endpoints send and receive (BRIEF2 API
// contract, Phase 3). Kept together because they only make sense together.

public class InvoiceView
{
    public Guid Id { get; set; }

    // "Pending", "Reviewed", "Committed" or "Discarded".
    public string Status { get; set; } = string.Empty;

    // True when the lines are the demo reader's, not a real reading.
    public bool IsDemoExtraction { get; set; }

    public string? SupplierName { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateOnly? InvoiceDate { get; set; }

    // Null when the invoice total could not be read.
    public long? ExtractedTotalMinor { get; set; }

    // The ticked lines added up, as the stock receipt would book them.
    public long LinesTotalMinor { get; set; }

    // False whenever ExtractedTotalMinor is null.
    public bool TotalsAgree { get; set; }

    // ExtractedTotalMinor minus LinesTotalMinor; 0 when the total is null.
    public long DifferenceMinor { get; set; }

    public List<InvoiceLineView> Lines { get; set; } = new();
}

public class InvoiceLineView
{
    public Guid Id { get; set; }
    public int LineNumber { get; set; }
    public string RawText { get; set; } = string.Empty;
    public string ProposedName { get; set; } = string.Empty;
    public decimal ProposedQuantity { get; set; }
    public long ProposedUnitCostMinor { get; set; }

    // Only ever set by the owner (or by a commit that created the part).
    // Never pre-set from a suggestion.
    public Guid? MatchedProductId { get; set; }

    public List<InvoiceSuggestionView> Suggestions { get; set; } = new();
    public bool Accepted { get; set; }
}

public class InvoiceSuggestionView
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public double Score { get; set; }
}

// PUT /api/supplier-invoices/{id}. The header fields are the new values:
// null clears one.
public class UpdateSupplierInvoiceRequest
{
    public string? SupplierName { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateOnly? InvoiceDate { get; set; }
    public List<InvoiceLineEdit> Lines { get; set; } = new();
}

// One line as the owner left it. A line not sent is left as it was.
public class InvoiceLineEdit
{
    public Guid Id { get; set; }
    public string? ProposedName { get; set; }
    public decimal ProposedQuantity { get; set; }
    public long ProposedUnitCostMinor { get; set; }
    public Guid? MatchedProductId { get; set; }

    // Make a new part from this line when committing. There is no column for
    // these two, so they only count when sent with the commit itself.
    public bool CreateNew { get; set; }
    public long? NewSellPriceMinor { get; set; }

    public bool Accepted { get; set; }
}

// POST /api/supplier-invoices/{id}/commit. Lines, when sent, are applied
// exactly as a PUT would, first; they are the only way to send CreateNew.
public class CommitSupplierInvoiceRequest
{
    // "Cash", "Mpesa" (or "MPesa") or "Credit".
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PaymentMethod? PaidWith { get; set; }

    // When the goods and the debt arrived. Left out, it is the invoice's own
    // date. If that falls in a period with a filed return, the commit is
    // refused (409, closedPeriod) and may be sent again with today's date.
    public DateOnly? ReceiptDate { get; set; }

    public List<InvoiceLineEdit>? Lines { get; set; }
}

public class CommitSupplierInvoiceResult
{
    public Guid StockReceiptId { get; set; }
    public Guid JournalEntryId { get; set; }
    public int LinesReceived { get; set; }
}

// What the service answers. Exactly one of these is set.
public class InvoiceOutcome<T> where T : class
{
    public bool NotFound { get; set; }

    // 409: the invoice is already committed or discarded.
    public string? Conflict { get; set; }

    // 400: what is wrong, in words.
    public string? Error { get; set; }

    // 409 with closedPeriod: the receipt date falls in a period whose tax
    // return has been filed, so the books refused it. Error holds the
    // sentence; nothing was written.
    public bool ClosedPeriod { get; set; }

    public T? Value { get; set; }
}

// The answer to GET /api/supplier-invoices/reader-status.
public class InvoiceReaderStatus
{
    // True only when a real, built invoice reader is configured.
    public bool Real { get; set; }
}
