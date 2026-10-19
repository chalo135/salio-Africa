namespace Salio.Api.Dtos;

public class SignedOutDevicesResponse
{
    // How many accounts in this shop had their logins killed. Everyone must
    // sign in again, and nobody's password changed.
    public int AccountsSignedOut { get; set; }
}
