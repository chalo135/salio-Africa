using System.ComponentModel.DataAnnotations;
using Salio.Domain.Enums;

namespace Salio.Api.Dtos;

public class ChangeRoleRequest
{
    // 0 = Owner, 1 = Cashier.
    [Required]
    public MembershipRole Role { get; set; }
}
