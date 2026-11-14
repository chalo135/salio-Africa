namespace Salio.Api.Dtos;

public class OpeningStockResponse
{
    // Ties the stock movements and the journal entry together: it is the
    // journal entry's SourceId, and in every movement's note.
    public Guid Id { get; set; }

    public DateOnly OpeningDate { get; set; }

    // What the shelf is worth: the amount added to Inventory and to
    // Owner's Equity.
    public long TotalCostMinor { get; set; }

    public int LineCount { get; set; }

    public Guid JournalEntryId { get; set; }
}
