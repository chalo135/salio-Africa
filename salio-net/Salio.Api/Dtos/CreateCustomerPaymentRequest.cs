using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Salio.Domain.Enums;

namespace Salio.Api.Dtos;

public class CreateCustomerPaymentRequest
{
    public Guid? OrganizationId { get; set; }

    [Required]
    public Guid CustomerId { get; set; }

    public long AmountMinor { get; set; }

    [Required]
    public DateOnly PaymentDate { get; set; }

    // Cash (0) or Mpesa (1). Accepts the name or the number.
    [Required]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PaymentMethod PaidWith { get; set; }
}
