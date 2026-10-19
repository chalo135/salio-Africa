using System.Text.Json.Serialization;
using Salio.Domain.Enums;

namespace Salio.Api.Dtos;

// Money collected from a credit customer.
public class CustomerPaymentResponse
{
    // The journal entry's id.
    public Guid Id { get; set; }

    public Guid CustomerId { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public DateOnly PaymentDate { get; set; }

    public long AmountMinor { get; set; }

    // "Cash" or "Mpesa".
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PaymentMethod PaidWith { get; set; }

    // What the customer still owes after this payment. Only on the answer
    // to POST: a list of past payments has no single "now" to report.
    public long? BalanceAfterMinor { get; set; }
}
