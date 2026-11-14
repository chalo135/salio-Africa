namespace Salio.Domain.Enums
{
    // What the owner said about tax when they set the shop up. Stored as a
    // number: new values go at the end.
    public enum TaxPosition
    {
        TurnoverTax,
        VatRegistered,

        // A first-class answer, not a gap: most shopkeepers genuinely do not
        // know, and refusing to let them past would lose the setup.
        NotSure
    }
}
