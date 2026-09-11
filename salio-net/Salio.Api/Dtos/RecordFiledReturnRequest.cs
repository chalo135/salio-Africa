using System.ComponentModel.DataAnnotations;
using Salio.Domain.Enums;

namespace Salio.Api.Dtos;

public class RecordFiledReturnRequest
{
    // TODO: must come from the authenticated user once authentication exists.
    [Required]
    public Guid OrganizationId { get; set; }

    [Required]
    public DateOnly PeriodFrom { get; set; }

    [Required]
    public DateOnly PeriodTo { get; set; }

    [Required]
    public TaxType TaxType { get; set; }

    // What she actually paid, which may differ from Salio's figure.
    public long AmountMinor { get; set; }

    // When she submitted on iTax, not when she told Salio about it.
    [Required]
    public DateTimeOffset FiledAt { get; set; }

    [Required]
    [MaxLength(100)]
    public string AcknowledgementReference { get; set; } = string.Empty;
}
