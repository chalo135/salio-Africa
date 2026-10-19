namespace Salio.Api.Dtos;

// Who is signed in, and which shops they may work in. Always a list, even
// for one shop: the till decides whether to show a chooser, and no shop is
// ever picked for the user here.
public class SignedInUserResponse
{
    public Guid Id { get; set; }

    public string FullName { get; set; } = string.Empty;

    public List<ShopMembershipResponse> Shops { get; set; } = new();
}
