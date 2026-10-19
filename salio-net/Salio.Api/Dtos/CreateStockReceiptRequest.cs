using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Salio.Domain.Enums;

namespace Salio.Api.Dtos;

public class CreateStockReceiptRequest
{
    // Optional, as with a sale: the server still checks the caller belongs
    // to it (locked rule 9).
    public Guid? OrganizationId { get; set; }

    [Required]
    public DateOnly ReceiptDate { get; set; }

    // "Cash", "MPesa" or "Credit" (owed to the supplier). Read as a name, not
    // a number; matching the name ignores case, so "MPesa" and "Mpesa" both work.
    [Required]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PaymentMethod PaidWith { get; set; }

    // Free text. There is no supplier list yet.
    [MaxLength(200)]
    public string? SupplierName { get; set; }

    [Required]
    public List<CreateStockReceiptLineRequest> Lines { get; set; } = new();
}
