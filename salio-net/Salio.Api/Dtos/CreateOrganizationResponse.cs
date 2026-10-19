namespace Salio.Api.Dtos;

public class CreateOrganizationResponse
{
    public Guid OrganizationId { get; set; }

    public string ShopName { get; set; } = string.Empty;

    public Guid OwnerId { get; set; }

    public string OwnerEmail { get; set; } = string.Empty;

    // How many accounts the chart of accounts started with.
    public int AccountsCreated { get; set; }
}
