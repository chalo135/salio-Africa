using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Salio.Domain.Enums;

namespace Salio.Api.Dtos;

public class CreateExpenseRequest
{
    public Guid? OrganizationId { get; set; }

    [Required]
    public DateOnly ExpenseDate { get; set; }

    // A shop cost (an expense account) or Drawings (3100).
    [Required]
    public Guid AccountId { get; set; }

    public long AmountMinor { get; set; }

    // Cash (0) or Mpesa (1). Accepts the name or the number.
    [Required]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PaymentMethod PaidFrom { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }
}
