namespace Salio.Api.Dtos;

// One customer's credit sales and payments, oldest first, with the balance
// after each. Worked out from journal lines on 1100 every time it is read.
public class CustomerStatementResponse
{
    public Guid CustomerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public long BalanceMinor { get; set; }

    public List<CustomerStatementLineResponse> Lines { get; set; } = new();
}
