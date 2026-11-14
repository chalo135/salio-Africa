using Salio.Domain.Enums;

namespace Salio.Domain.Services;

public class ImportStockBasisDecision
{
    // Null when the answer is acceptable. Otherwise the sentence to show;
    // nothing is imported.
    public string? Error { get; set; }

    public ImportStockBasis Basis { get; set; }

    // Set only for a Purchase.
    public PaymentMethod? PaidWith { get; set; }
}

// What the quantities in an import file mean. Quantity alone cannot tell
// stock the owner already had from stock the shop bought, so the owner is
// asked, and this checks the answer. No database: the caller says whether
// the file carries any quantity.
public static class ImportStockBasisRules
{
    // stockBasis: "OpeningStock", "Purchase" or "None", any capitals.
    // paidWith: the PaymentMethod number the Stock in screen sends
    // (0 Cash, 1 Mpesa, 3 Credit).
    public static ImportStockBasisDecision Decide(bool hasQuantities, string? stockBasis, string? paidWith)
    {
        // No quantities: nothing reaches the books, whatever was sent.
        if (!hasQuantities)
        {
            return new ImportStockBasisDecision { Basis = ImportStockBasis.None };
        }

        string basisText = (stockBasis ?? "").Trim();

        if (basisText.Length == 0 || string.Equals(basisText, "None", StringComparison.OrdinalIgnoreCase))
        {
            return Refused(
                "This file has quantities. Say whether they are stock you already had when you started, "
                + "or stock you bought.");
        }

        if (string.Equals(basisText, "OpeningStock", StringComparison.OrdinalIgnoreCase))
        {
            return new ImportStockBasisDecision { Basis = ImportStockBasis.OpeningStock };
        }

        if (!string.Equals(basisText, "Purchase", StringComparison.OrdinalIgnoreCase))
        {
            return Refused(
                "Salio did not understand which kind of stock this is. Choose stock you already had, "
                + "or stock you bought.");
        }

        // A purchase: how was it paid?
        string paidText = (paidWith ?? "").Trim();

        if (paidText.Length == 0)
        {
            return Refused("Say how this stock was paid for: Cash, M-Pesa or on credit.");
        }

        if (!int.TryParse(paidText, out int paidNumber))
        {
            return Refused("Paid with must be Cash, M-Pesa or Credit.");
        }

        var method = (PaymentMethod)paidNumber;

        // The three ways StockReceiptWriter knows how to book. Card is not
        // one of them.
        if (method != PaymentMethod.Cash && method != PaymentMethod.Mpesa && method != PaymentMethod.Credit)
        {
            return Refused("Paid with must be Cash, M-Pesa or Credit.");
        }

        return new ImportStockBasisDecision { Basis = ImportStockBasis.Purchase, PaidWith = method };
    }

    private static ImportStockBasisDecision Refused(string error) => new() { Error = error };
}
