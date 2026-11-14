namespace Salio.Api.Dtos;

// What a password login, a PIN unlock or finishing setup sends back: who is
// signed in, their shops, and whether this phone has a PIN yet. After a
// password login it is true when this phone was already this person's
// trusted device with a PIN, so no new PIN is asked for.
public class LoginResponse : SignedInUserResponse
{
    public bool PinSet { get; set; }
}
