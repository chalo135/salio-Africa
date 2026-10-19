namespace Salio.Api.Dtos;

public class CustomerResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    // What they owe right now, worked out from the journal. 0 if nothing.
    public long BalanceMinor { get; set; }
}
