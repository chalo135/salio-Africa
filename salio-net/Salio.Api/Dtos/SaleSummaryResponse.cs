using Salio.Domain.Enums;

namespace Salio.Api.Dtos;

// One line of a sales list. No cost, no profit: those are the Owner's
// business and live in the reports (locked rule 13).
public class SaleSummaryResponse
{
    public Guid Id { get; set; }

    public string SaleNumber { get; set; } = string.Empty;

    public DateOnly SaleDate { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public long TotalMinor { get; set; }

    public SaleStatus Status { get; set; }

    // Who sold it. Null for sales made before logins existed.
    public Guid? SoldByUserId { get; set; }

    public string? SoldBy { get; set; }

    // How the customer paid. More than one entry is a split payment.
    public List<SalePaymentResponse> Payments { get; set; } = new();
}
