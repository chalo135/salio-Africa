using System.Text.Json.Serialization;
using Salio.Domain.Enums;

namespace Salio.Api.Dtos;

// One payment out, as the expenses screen lists it.
public class ExpenseResponse
{
    // The journal entry's id.
    public Guid Id { get; set; }

    public DateOnly ExpenseDate { get; set; }

    public Guid AccountId { get; set; }

    public string CategoryName { get; set; } = string.Empty;

    // True for money the family took out. A screen must total these apart
    // from shop costs, never together.
    public bool IsDrawings { get; set; }

    public long AmountMinor { get; set; }

    // "Cash" or "Mpesa".
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PaymentMethod PaidFrom { get; set; }

    public string? Note { get; set; }
}
