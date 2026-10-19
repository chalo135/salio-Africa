using Salio.Domain.Enums;

namespace Salio.Api.Dtos;

public class SaleResponse
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    // The receipt number the customer sees.
    public string SaleNumber { get; set; } = string.Empty;

    public DateOnly SaleDate { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public long TotalMinor { get; set; }

    public SaleStatus Status { get; set; }

    public Guid JournalEntryId { get; set; }

    // A credit sale only: who owes it, and what they owe now, this sale
    // included. Null for cash and M-Pesa.
    public Guid? CustomerId { get; set; }

    public string? CustomerName { get; set; }

    public long? CustomerBalanceMinor { get; set; }
}
