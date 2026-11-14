namespace Salio.Api.Dtos;

// The code a stranger typed, with or without the dash, any case. No
// validation attributes on purpose: a blank or overlong code gets the same
// refusal as any other code that does not work.
public class CheckSetupCodeRequest
{
    public string Code { get; set; } = string.Empty;
}
