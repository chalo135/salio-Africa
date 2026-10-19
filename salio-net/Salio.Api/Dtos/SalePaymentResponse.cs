using System.Text.Json.Serialization;
using Salio.Domain.Enums;

namespace Salio.Api.Dtos;

// One tender against a sale. A sale paid half cash, half M-Pesa has two.
public class SalePaymentResponse
{
    // Serialised as its name — "Cash", "Mpesa" — not as a number a screen
    // would have to keep its own lookup table for. The property stays the
    // enum, so the server still cannot hold a method that does not exist.
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PaymentMethod Method { get; set; }

    public long AmountMinor { get; set; }
}
