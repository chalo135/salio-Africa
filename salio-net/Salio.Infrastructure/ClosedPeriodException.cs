using Salio.Domain.Enums;

namespace Salio.Infrastructure;

// Thrown by JournalPoster when an entry is dated inside a period whose tax
// return has been filed. It IS an InvalidOperationException, so every
// existing catch still catches it with the same message; a caller that wants
// to offer "post it today instead" can catch this type and read the date.
public class ClosedPeriodException : InvalidOperationException
{
    public ClosedPeriodException(DateOnly entryDate, TaxType taxType, string message)
        : base(message)
    {
        EntryDate = entryDate;
        TaxType = taxType;
    }

    // The date the refused entry carried.
    public DateOnly EntryDate { get; }

    // Which return was filed for that period.
    public TaxType TaxType { get; }
}
