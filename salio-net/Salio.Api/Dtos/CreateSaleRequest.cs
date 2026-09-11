using System.ComponentModel.DataAnnotations;

namespace Salio.Api.Dtos;

public class CreateSaleRequest
{
    // TODO: must come from the authenticated user once authentication exists.
    [Required]
    public Guid OrganizationId { get; set; }

    [Required]
    public DateOnly SaleDate { get; set; }

    [Required]
    public List<CreateSaleLineRequest> Lines { get; set; } = new();

    // How the customer paid. More than one entry is a split payment.
    [Required]
    public List<CreatePaymentRequest> Payments { get; set; } = new();
}
