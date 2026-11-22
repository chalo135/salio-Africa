namespace Salio.Api.Dtos;

// What changed in the shop since the Owner last pressed Hide, or in the last
// 7 days before the first Hide. Rows is empty when there is nothing to say,
// and the card then shows nothing at all.
public class WelcomeCardResponse
{
    // Changes after this are shown: "7 things changed since Tuesday".
    public DateTimeOffset Since { get; set; }

    // Every row found, before the cap of 5.
    public int Total { get; set; }

    // Every part named in any row, for "See everything that changed".
    public List<Guid> ProductIds { get; set; } = new();

    // The newest 5.
    public List<WelcomeCardRow> Rows { get; set; } = new();
}

public class WelcomeCardRow
{
    // "costUp", "priceChanged" or "ranOut".
    public string Kind { get; set; } = string.Empty;

    public Guid ProductId { get; set; }
    public string PartName { get; set; } = string.Empty;
    public DateTimeOffset At { get; set; }

    // costUp: the delivery costs. priceChanged: the selling prices.
    public long? WasMinor { get; set; }
    public long? NowMinor { get; set; }

    // costUp: what the part sells at today, and what one earns at that
    // price on the old cost and on the new one.
    public long? AskingMinor { get; set; }
    public long? KeptWasMinor { get; set; }
    public long? KeptNowMinor { get; set; }

    // priceChanged: who changed it, first name only.
    public string? ChangedBy { get; set; }
}
