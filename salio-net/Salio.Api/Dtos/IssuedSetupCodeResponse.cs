namespace Salio.Api.Dtos;

// A code just issued: what the admin copies and sends by hand.
public class IssuedSetupCodeResponse
{
    public Guid Id { get; set; }

    // Shown with the dash, e.g. "K7M-P29".
    public string Code { get; set; } = string.Empty;

    public string Link { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }

    // Ready to paste into WhatsApp.
    public string Message { get; set; } = string.Empty;
}
