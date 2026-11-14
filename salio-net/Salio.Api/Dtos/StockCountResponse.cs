namespace Salio.Api.Dtos;

public class StockCountResponse
{
    // Ties the stock movements and the journal entry together: it is the
    // journal entry's SourceId, and in every movement's note. Null when the
    // count matched everywhere and nothing was written.
    public Guid? Id { get; set; }

    public DateOnly CountDate { get; set; }

    // How many lines were sent, and how many of them differed.
    public int LinesCounted { get; set; }
    public int LinesAdjusted { get; set; }

    // What the missing stock was worth, and what the found stock was worth.
    // Both are zero or more.
    public long MissingValueMinor { get; set; }
    public long FoundValueMinor { get; set; }

    // Null when no journal entry was written.
    public Guid? JournalEntryId { get; set; }

    // One plain sentence saying what happened, worth showing on screen.
    public string Message { get; set; } = "";

    public List<StockCountLineResponse> Lines { get; set; } = new();
}
