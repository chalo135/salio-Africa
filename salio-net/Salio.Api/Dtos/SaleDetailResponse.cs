namespace Salio.Api.Dtos;

// One sale, as a receipt reads. No cost and no profit anywhere in it.
public class SaleDetailResponse
{
    public Guid Id { get; set; }

    public string SaleNumber { get; set; } = string.Empty;

    public DateOnly SaleDate { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public long TotalMinor { get; set; }

    public List<SalePaymentResponse> Payments { get; set; } = new();

    // Null for sales made before logins existed.
    public string? SoldBy { get; set; }

    public List<SaleLineResponse> Lines { get; set; } = new();

    // Who took it on credit. Null unless it was a credit sale.
    public string? CustomerName { get; set; }
}
