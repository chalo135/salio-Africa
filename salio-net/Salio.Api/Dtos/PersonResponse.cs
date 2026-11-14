using Salio.Domain.Enums;

namespace Salio.Api.Dtos;

// One person in this shop. Never a hash, never a password.
public class PersonResponse
{
    public Guid UserId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    // 0 = Owner, 1 = Cashier.
    public MembershipRole Role { get; set; }

    // False if switched off in this shop, or their whole account is off.
    public bool IsActive { get; set; }

    // True until they replace the temporary password they were given.
    public bool MustChangePassword { get; set; }

    // The Owner looking at the list, so the screen can hide what they may
    // not do to themselves.
    public bool IsYou { get; set; }
}
