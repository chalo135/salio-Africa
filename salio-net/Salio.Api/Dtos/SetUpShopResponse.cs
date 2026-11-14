namespace Salio.Api.Dtos;

// No password, no code. The owner is signed in by this same call, so the
// screen does not ask for the password again.
public class SetUpShopResponse
{
    public Guid OrganizationId { get; set; }

    public string ShopName { get; set; } = string.Empty;

    public string OwnerEmail { get; set; } = string.Empty;

    // The owner, signed in and with this phone trusted: the same shape a
    // password login returns. Null only if the shop was made but signing in
    // then failed; the screen then signs in with /api/auth/login as before.
    public LoginResponse? Login { get; set; }
}
