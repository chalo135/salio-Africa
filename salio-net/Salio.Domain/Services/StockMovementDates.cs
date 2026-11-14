namespace Salio.Domain.Services;

// One transaction, one date. A stock movement carries a moment
// (OccurredAt), its journal entry carries a business date (EntryDate). They
// must always tell the same day, or the stock report and the books disagree
// about when goods arrived. QA once saw an invoice whose entry said 07 Nov
// while its stock movements said 09 Nov, because the movements were stamped
// with the time the button was pressed.
//
// Every writer that posts a journal entry and stock movements together gets
// the movements' moment from here, passing the entry's own date.
public static class StockMovementDates
{
    // Kenya has one time zone (UTC+3) and no daylight saving.
    public static readonly TimeSpan KenyaOffset = TimeSpan.FromHours(3);

    // The day a moment falls on in Kenya.
    public static DateOnly KenyaDateOf(DateTimeOffset moment)
    {
        return DateOnly.FromDateTime(moment.ToOffset(KenyaOffset).DateTime);
    }

    // The moment to stamp on the stock movements of an entry dated entryDate.
    //
    // - Entry dated today: the real moment, now, exactly as before.
    // - Entry dated another day (an invoice dated last Tuesday): the last
    //   second of that day in Kenya. Late in the day, so the goods land after
    //   anything else already recorded on that same day.
    //
    // Always returned in UTC, which is how the database stores it.
    public static DateTimeOffset OccurredAtFor(DateOnly entryDate, DateTimeOffset now)
    {
        if (KenyaDateOf(now) == entryDate)
        {
            return now.ToUniversalTime();
        }

        var lastSecond = new DateTimeOffset(
            entryDate.ToDateTime(new TimeOnly(23, 59, 59)), KenyaOffset);

        return lastSecond.ToUniversalTime();
    }
}
