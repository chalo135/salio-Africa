using Salio.Domain.Enums;

namespace Salio.Api.Dtos;

public class FiledReturnResponse
{
    public Guid Id { get; set; }

    public Guid OrganizationId { get; set; }

    public DateOnly PeriodFrom { get; set; }

    public DateOnly PeriodTo { get; set; }

    public TaxType TaxType { get; set; }

    public long AmountMinor { get; set; }

    public DateTimeOffset FiledAt { get; set; }

    public string AcknowledgementReference { get; set; } = string.Empty;
}
