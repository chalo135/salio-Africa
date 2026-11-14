namespace Salio.Api.Dtos;

// One row of the admin's list of setup codes.
public class AdminSetupCodeResponse
{
    public Guid Id { get; set; }

    // Shown with the dash, e.g. "K7M-P29".
    public string Code { get; set; } = string.Empty;

    public string ShopName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RedeemedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    // "Unused", "Redeemed", "Expired" or "Revoked".
    public string Status { get; set; } = string.Empty;

    // The shop the code made, once it has been used.
    public Guid? OrganizationId { get; set; }
}
