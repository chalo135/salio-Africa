namespace Salio.Api.Dtos;

// Only the shop name the code was issued for, to fill in the first answer.
// Never the email (decision D10).
public class CheckSetupCodeResponse
{
    public string ShopName { get; set; } = string.Empty;
}
