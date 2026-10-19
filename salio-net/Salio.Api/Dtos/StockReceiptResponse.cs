namespace Salio.Api.Dtos;

public class StockReceiptResponse
{
    // Ties the stock movements and the journal entry together: it is the
    // journal entry's SourceId, and in every movement's note.
    public Guid Id { get; set; }

    public DateOnly ReceiptDate { get; set; }

    public long TotalCostMinor { get; set; }

    public int LineCount { get; set; }

    public Guid JournalEntryId { get; set; }
}
