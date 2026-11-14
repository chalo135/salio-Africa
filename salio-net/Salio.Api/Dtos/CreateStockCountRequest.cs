using System.ComponentModel.DataAnnotations;

namespace Salio.Api.Dtos;

// What the owner found on the shelf when counting. It need not cover the
// whole shop: one shelf at a time is normal.
public class CreateStockCountRequest
{
    // Optional, as with a sale: the server still checks the caller belongs
    // to it (locked rule 9).
    public Guid? OrganizationId { get; set; }

    // The day the count was taken. The journal entry is dated this day.
    [Required]
    public DateOnly CountDate { get; set; }

    [Required]
    public List<CreateStockCountLineRequest> Lines { get; set; } = new();
}
