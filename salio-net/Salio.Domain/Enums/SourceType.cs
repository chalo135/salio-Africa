namespace Salio.Domain.Enums
{
    // Stored as a number. New values go at the end, so the numbers already
    // in the database keep their meaning.
    public enum SourceType
    {
        Manual,
        Sale,
        Purchase,
        StockAdjustment,

        // Money going out for the shop: rent, power, wages.
        Expense,

        // Money the owner takes out for the family. Kept apart from Expense
        // so nothing can ever add the two together by mistake.
        Drawings,

        // A credit customer paying off what they owe.
        CustomerPayment
    }
}
