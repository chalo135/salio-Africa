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
}
