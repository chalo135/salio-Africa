namespace Salio.Api.Contracts;

public class JournalLineResponse
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public long DebitMinor { get; set; }

    public long CreditMinor { get; set; }

    public string Currency { get; set; } = string.Empty;
}
