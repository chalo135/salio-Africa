namespace Salio.Api.Dtos;

public class CustomerStatementLineResponse
{
    public DateOnly Date { get; set; }

    // "Sale" or "Payment".
    public string Kind { get; set; } = string.Empty;

    // The sale number for a sale; "Cash" or "Mpesa" for a payment.
    public string Reference { get; set; } = string.Empty;

    // What this added to the debt (a sale) or took off it (a payment).
    // Exactly one is non-zero.
    public long ChargedMinor { get; set; }

    public long PaidMinor { get; set; }

    public long BalanceAfterMinor { get; set; }
}
