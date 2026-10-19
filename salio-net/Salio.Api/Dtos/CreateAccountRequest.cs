using System.ComponentModel.DataAnnotations;

namespace Salio.Api.Dtos;

// A shopkeeper's own spending category. A name only: Salio picks the code
// and the class, so this can only ever make an expense account.
public class CreateAccountRequest
{
    public Guid? OrganizationId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;
}
