namespace Salio.Api.Dtos;

// Who this trusted phone belongs to, for the PIN screen's greeting.
public class DeviceResponse
{
    public string FullName { get; set; } = string.Empty;

    public bool PinSet { get; set; }
}
