using System.ComponentModel.DataAnnotations;
using Salio.Domain.Enums;

namespace Salio.Api.Dtos;

public class CreatePaymentRequest
{
    [Required]
    public PaymentMethod Method { get; set; }

    public long AmountMinor { get; set; }

    // The M-Pesa confirmation code, typed in at the till. Null for cash.
    [MaxLength(50)]
    public string? Reference { get; set; }
}
