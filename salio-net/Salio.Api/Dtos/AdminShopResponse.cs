namespace Salio.Api.Dtos;

// One row of the admin's list of shops. Deliberately thin: the admin area
// never shows a shop's money, stock, tax, customers or books. The date of
// the last sale is the only thing read from its sales (decision D8).
public class AdminShopResponse
{
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = string.Empty;

    // The first active Owner's email. Null if the shop has none.
    public string? OwnerEmail { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    // Null if the shop has never recorded a sale.
    public DateTimeOffset? LastSaleAt { get; set; }
}
