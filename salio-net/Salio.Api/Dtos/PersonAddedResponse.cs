namespace Salio.Api.Dtos;

public class PersonAddedResponse
{
    public PersonResponse Person { get; set; } = new();

    // True when the email already had a Salio account, so this shop was
    // added to it instead of a second account being made.
    public bool AddedExistingAccount { get; set; }

    // Shown ONCE, here, and never again: only its hash is stored. Null when
    // an existing account was added, because they keep their own password.
    public string? TemporaryPassword { get; set; }

    // A sentence for the Owner saying what happened.
    public string Message { get; set; } = string.Empty;
}
