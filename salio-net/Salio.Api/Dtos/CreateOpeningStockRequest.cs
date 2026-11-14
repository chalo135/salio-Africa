using System.ComponentModel.DataAnnotations;

namespace Salio.Api.Dtos;

// What a shop already has on its shelves on the day it starts using Salio.
public class CreateOpeningStockRequest
{
    // Optional, as with a sale: the server still checks the caller belongs
    // to it (locked rule 9).
    public Guid? OrganizationId { get; set; }

    // The day the count was taken. The journal entry is dated this day.
    [Required]
    public DateOnly OpeningDate { get; set; }

    [Required]
    public List<CreateOpeningStockLineRequest> Lines { get; set; } = new();
}
