using System.ComponentModel.DataAnnotations;

namespace Salio.Api.Dtos;

public class CreateSaleRequest
{
    // Optional. With one shop the server knows which it is; if it is sent,
    // the server still checks the caller belongs to it (locked rule 9).
    public Guid? OrganizationId { get; set; }

    [Required]
    public DateOnly SaleDate { get; set; }

    [Required]
    public List<CreateSaleLineRequest> Lines { get; set; } = new();

    // How the customer paid. More than one entry is a split payment.
    // Credit (method 3) must be the only payment, for the whole total.
    [Required]
    public List<CreatePaymentRequest> Payments { get; set; } = new();

    // Who takes the goods on credit. Required for a credit sale, ignored
    // otherwise. Must be a customer of this shop.
    public Guid? CustomerId { get; set; }

    // The id the till made for this checkout. Sending the same id twice
    // returns the first sale rather than booking a second one. Optional, so
    // an older till or a script still works without it.
    public Guid? ClientRequestId { get; set; }
}
