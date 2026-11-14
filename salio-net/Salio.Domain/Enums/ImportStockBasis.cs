namespace Salio.Domain.Enums
{
    // What the quantities in an import file mean. Stored as a number. New
    // values go at the end, so the numbers already in the database keep
    // their meaning.
    //
    // None is first on purpose: it is the default value, so a batch nobody
    // labelled can never claim to be owner's equity by accident.
    public enum ImportStockBasis
    {
        // The file carried no quantities. Parts were created or updated only;
        // no stock and no journal entry was posted.
        None,

        // Stock the owner already had when the books opened. Posted through
        // OpeningStockWriter: Dr Inventory, Cr Owner's Equity.
        OpeningStock,

        // Stock the shop bought. Posted through StockReceiptWriter as one
        // receipt, paid as PaidWith says. Never equity.
        Purchase
    }
}
